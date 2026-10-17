using Xunit;

namespace BridgeBuilder.Tests
{
    public class CoreAssemblyTests
    {
        [Fact]
        public void Core_sources_compile_into_the_shared_assembly()
        {
            Assert.Equal("BridgeBuilder.Core", CoreAssembly.Name);
        }
    }
}
