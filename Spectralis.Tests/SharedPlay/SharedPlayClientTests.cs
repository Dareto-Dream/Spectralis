using System;
using System.Threading.Tasks;
using Spectralis.App.Services;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class SharedPlayClientTests
    {
        [Fact]
        public void IsConnected_WhenNotConnected_ReturnsFalse()
        {
            var client = new SharedPlayClient("ws://localhost:9999");
            Assert.False(client.IsConnected);
        }

        [Fact]
        public void SessionId_WhenNotConnected_IsNull()
        {
            var client = new SharedPlayClient("ws://localhost:9999");
            Assert.Null(client.SessionId);
        }

        [Fact]
        public void Dispose_WhenNotConnected_DoesNotThrow()
        {
            var client = new SharedPlayClient("ws://localhost:9999");
            var ex = Record.Exception(() => client.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var client = new SharedPlayClient("ws://localhost:9999");
            client.Dispose();
            var ex = Record.Exception(() => client.Dispose());
            Assert.Null(ex);
        }
    }
}
