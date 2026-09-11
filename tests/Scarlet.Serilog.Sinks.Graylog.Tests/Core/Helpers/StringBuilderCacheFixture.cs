using Scarlet.Serilog.Sinks.Graylog.Core.Helpers;
using System.Text;
using Xunit;

namespace Scarlet.Serilog.Sinks.Graylog.Tests.Core.Helpers
{
    /// <summary>
    /// Tests for the thread-static <see cref="StringBuilder"/> cache backing
    /// <see cref="Core.MessageBuilders.ExceptionMessageBuilder"/> and
    /// <see cref="Core.MessageBuilders.GelfMessageBuilder"/>'s scratch buffers.
    /// </summary>
    public class StringBuilderCacheFixture
    {
        [Fact]
        public void Acquire_AfterRelease_ReturnsTheSameInstance()
        {
            StringBuilder first = StringBuilderCache<TestSlot>.Acquire();
            first.Append("leftover text");
            StringBuilderCache<TestSlot>.Release(first);

            StringBuilder second = StringBuilderCache<TestSlot>.Acquire();

            Assert.Same(first, second);
        }

        [Fact]
        public void Acquire_ClearsWhateverTheBuilderPreviouslyHeld()
        {
            StringBuilder first = StringBuilderCache<TestSlot>.Acquire();
            first.Append("leftover text");
            StringBuilderCache<TestSlot>.Release(first);

            StringBuilder second = StringBuilderCache<TestSlot>.Acquire();

            Assert.Equal(0, second.Length);
        }

        [Fact]
        public void Acquire_WhenNothingIsCached_ReturnsAnEmptyBuilder()
        {
            // Nothing was released on this test's thread beforehand, so this exercises the
            // never-cached path directly rather than relying on state left over from another test.
            var target = new StringBuilder();
            StringBuilderCache<TestSlot>.Release(target);
            StringBuilder first = StringBuilderCache<TestSlot>.Acquire();
            StringBuilder second = StringBuilderCache<TestSlot>.Acquire();

            // The slot was already emptied by the first Acquire, so the second call - still on the
            // same thread - gets a brand new instance rather than the one just handed out.
            Assert.NotSame(first, second);
            Assert.Equal(0, second.Length);
        }

        [Fact]
        public void Release_WhenTheBuilderOutgrewTheCap_IsNotCachedBack()
        {
            var oversized = new StringBuilder();
            oversized.Append('x', 100_000);

            StringBuilderCache<TestSlot>.Release(oversized);

            StringBuilder acquired = StringBuilderCache<TestSlot>.Acquire();

            Assert.NotSame(oversized, acquired);
        }

        [Fact]
        public void GetStringAndRelease_ReturnsTheBuilderText_AndReleasesIt()
        {
            StringBuilder builder = StringBuilderCache<TestSlot>.Acquire();
            builder.Append("hello");

            string text = StringBuilderCache<TestSlot>.GetStringAndRelease(builder);

            Assert.Equal("hello", text);
            Assert.Same(builder, StringBuilderCache<TestSlot>.Acquire());
        }

        // ReSharper disable once ClassNeverInstantiated.Local
        private sealed class TestSlot
        {
        }
    }
}
