using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>
    /// A bridge fixture file (spec section 5): geometry in room units, Y up. JSON names are the
    /// camelCase contract property names and a <see cref="Vec3"/> is written <c>[x, y, z]</c>.
    /// Unknown properties are ignored, so a fixture can carry extra fields that its own tests read
    /// with <see cref="FixtureLoader.LoadAs{T}"/> or <see cref="FixtureLoader.ReadDocument"/>.
    /// </summary>
    public sealed class BridgeFixture
    {
        public string Name { get; set; }

        public string Note { get; set; }

        public Node[] Nodes { get; set; } = Array.Empty<Node>();

        public Member[] Members { get; set; } = Array.Empty<Member>();

        public Fill[] Fills { get; set; } = Array.Empty<Fill>();

        public Line[] Lines { get; set; } = Array.Empty<Line>();

        public SectionProperties[] Sections { get; set; } = Array.Empty<SectionProperties>();

        public SolverInput ToSolverInput(double scaleFactor)
        {
            return new SolverInput
            {
                Nodes = Nodes,
                Members = Members,
                Fills = Fills,
                Sections = Sections,
                ScaleFactor = scaleFactor,
            };
        }
    }

    /// <summary>Reads fixture JSON copied from <c>tests/BridgeBuilder.Core.Tests/Fixtures</c> to the test output directory.</summary>
    public static class FixtureLoader
    {
        public static IReadOnlyList<string> StarterFixtureFileNames { get; } = new[]
        {
            "fixture-01-bare-cantilevers.json",
            "fixture-02-fixed-end-beams.json",
            "fixture-03-unbraced-rectangle.json",
            "fixture-04-buckling-columns.json",
            "fixture-05-triangulated-truss.json",
            "fixture-06-progressive-failure.json",
            "fixture-07-pier.json",
        };

        /// <summary>Read-only options used by every loader method: camelCase, case-insensitive, unknown members skipped, <see cref="Vec3JsonConverter"/>.</summary>
        public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

        public static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

        /// <summary>Absolute path of a fixture file in the test output directory.</summary>
        /// <exception cref="FileNotFoundException">The file was not copied to the output directory.</exception>
        public static string PathFor(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("A fixture file name is required.", nameof(fileName));
            }

            string path = Path.Combine(FixturesDirectory, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Fixture '" + fileName + "' is not in " + FixturesDirectory
                    + ". JSON files under tests/BridgeBuilder.Core.Tests/Fixtures are copied there on build.",
                    path);
            }

            return path;
        }

        public static string ReadText(string fileName)
        {
            return File.ReadAllText(PathFor(fileName));
        }

        /// <summary>Parses a fixture file as a <see cref="JsonDocument"/> for reading fields the contract types do not have. Dispose the result.</summary>
        public static JsonDocument ReadDocument(string fileName)
        {
            var options = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            };
            return JsonDocument.Parse(ReadText(fileName), options);
        }

        public static BridgeFixture Load(string fileName)
        {
            return LoadAs<BridgeFixture>(fileName);
        }

        /// <summary>Deserializes a fixture file into any type, using <see cref="SerializerOptions"/>.</summary>
        public static T LoadAs<T>(string fileName)
        {
            try
            {
                return Parse<T>(ReadText(fileName));
            }
            catch (JsonException exception)
            {
                throw new JsonException("Could not parse fixture '" + fileName + "': " + exception.Message, exception);
            }
        }

        public static BridgeFixture Parse(string json)
        {
            return Parse<BridgeFixture>(json);
        }

        public static T Parse<T>(string json)
        {
            T value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            if (value is null)
            {
                throw new JsonException("Fixture JSON is null.");
            }

            return value;
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                WriteIndented = true,
            };
            options.Converters.Add(new Vec3JsonConverter());
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }
    }

    /// <summary>Reads and writes a <see cref="Vec3"/> as a JSON array of exactly three numbers.</summary>
    public sealed class Vec3JsonConverter : JsonConverter<Vec3>
    {
        public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Expected a Vec3 as [x, y, z] but found " + reader.TokenType + ".");
            }

            var components = new double[3];
            for (int i = 0; i < components.Length; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
                {
                    throw new JsonException("A Vec3 needs exactly three numbers.");
                }

                components[i] = reader.GetDouble();
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("A Vec3 needs exactly three numbers.");
            }

            return new Vec3(components[0], components[1], components[2]);
        }

        public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }
}
