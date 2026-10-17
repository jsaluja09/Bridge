using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Linear static solve of the frame (spec 2.7), fixed supports only (spec 2.5): fixed nodes' six DOFs are
    /// removed exactly. Dense Gaussian elimination stays inside this file; the dynamic path uses banded storage.
    /// Positions are room units scaled by SolverInput.ScaleFactor; displacements come back in room units.
    /// </summary>
    public sealed class StaticSolver : IStaticSolver
    {
        public StaticSolveResult Solve(SolverInput input, NodalLoad[] loads)
        {
            int dof = Constants.Solver.DofPerNode;
            double scale = input.ScaleFactor > 0 ? input.ScaleFactor : 1.0;
            var index = new Dictionary<int, int>();
            for (int i = 0; i < input.Nodes.Count; i++)
                index[input.Nodes[i].Id] = i;
            var sections = new Dictionary<int, SectionProperties>();
            foreach (var s in input.Sections)
                sections[s.Id] = s;

            // Number the free DOFs.
            int n = input.Nodes.Count;
            var eq = new int[n * dof];
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                for (int d = 0; d < dof; d++)
                    eq[i * dof + d] = input.Nodes[i].Fixed ? -1 : count++;
            }

            var K = new double[count, count];
            var F = new double[count];
            foreach (var load in loads ?? Array.Empty<NodalLoad>())
            {
                if (!load.Force.IsFinite || !load.Moment.IsFinite)
                    throw new ArgumentException("Load is not finite");
                int b = index[load.NodeId] * dof;
                AddLoad(F, eq[b], load.Force.X); AddLoad(F, eq[b + 1], load.Force.Y); AddLoad(F, eq[b + 2], load.Force.Z);
                AddLoad(F, eq[b + 3], load.Moment.X); AddLoad(F, eq[b + 4], load.Moment.Y); AddLoad(F, eq[b + 5], load.Moment.Z);
            }

            foreach (var m in input.Members)
            {
                int ia = index[m.NodeA], ib = index[m.NodeB];
                var kg = FrameElement.WorldStiffness(sections[m.SectionId], input.Nodes[ia].Position * scale, input.Nodes[ib].Position * scale);
                for (int r = 0; r < 12; r++)
                {
                    int gr = eq[(r < 6 ? ia : ib) * dof + r % 6];
                    if (gr < 0)
                        continue;
                    for (int c = 0; c < 12; c++)
                    {
                        int gc = eq[(c < 6 ? ia : ib) * dof + c % 6];
                        if (gc >= 0)
                            K[gr, gc] += kg[r * 12 + c];
                    }
                }
            }

            var x = SolveDense(K, F);

            var u = new double[n * dof];
            for (int i = 0; i < n * dof; i++)
                u[i] = eq[i] >= 0 ? x[eq[i]] : 0.0;

            var result = new StaticSolveResult { Displacements = new Vec3[n], MemberForces = new MemberForce[input.Members.Count] };
            for (int i = 0; i < n; i++)
                result.Displacements[i] = Finite(new Vec3(u[i * dof], u[i * dof + 1], u[i * dof + 2]) / scale);
            for (int k = 0; k < input.Members.Count; k++)
            {
                var m = input.Members[k];
                int ia = index[m.NodeA], ib = index[m.NodeB];
                var ue = new double[12];
                for (int d = 0; d < 6; d++)
                {
                    ue[d] = u[ia * dof + d];
                    ue[6 + d] = u[ib * dof + d];
                }
                var f = FrameElement.EndForces(m.Id, sections[m.SectionId], input.Nodes[ia].Position * scale, input.Nodes[ib].Position * scale, ue);
                CheckFinite(f);
                result.MemberForces[k] = f;
            }
            return result;
        }

        static void AddLoad(double[] f, int e, double v)
        {
            if (e >= 0)
                f[e] += v;
        }

        static Vec3 Finite(Vec3 v)
        {
            if (!v.IsFinite)
                throw new InvalidOperationException("Static solve produced a non-finite displacement");
            return v;
        }

        static void CheckFinite(MemberForce f)
        {
            if (!double.IsFinite(f.Axial) || !double.IsFinite(f.MomentLocalYAtA) || !double.IsFinite(f.MomentLocalYAtB) ||
                !double.IsFinite(f.MomentLocalZAtA) || !double.IsFinite(f.MomentLocalZAtB))
                throw new InvalidOperationException("Static solve produced a non-finite force");
        }

        /// <summary>Gaussian elimination with partial pivoting. Throws if the structure is a mechanism.</summary>
        static double[] SolveDense(double[,] A, double[] b)
        {
            int n = b.Length;
            var a = (double[,])A.Clone();
            var x = (double[])b.Clone();
            double scale = 1;
            for (int i = 0; i < n; i++)
                scale = Math.Max(scale, Math.Abs(a[i, i]));
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                        pivot = r;
                }
                if (Math.Abs(a[pivot, col]) < 1e-12 * scale)
                    throw new InvalidOperationException("Structure is a mechanism (singular stiffness)");
                if (pivot != col)
                {
                    for (int c = 0; c < n; c++)
                        (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                    (x[col], x[pivot]) = (x[pivot], x[col]);
                }
                for (int r = col + 1; r < n; r++)
                {
                    double f = a[r, col] / a[col, col];
                    if (f == 0)
                        continue;
                    for (int c = col; c < n; c++)
                        a[r, c] -= f * a[col, c];
                    x[r] -= f * x[col];
                }
            }
            for (int r = n - 1; r >= 0; r--)
            {
                double sum = x[r];
                for (int c = r + 1; c < n; c++)
                    sum -= a[r, c] * x[c];
                x[r] = sum / a[r, r];
            }
            return x;
        }
    }
}
