using Serilog.Events;
using Serilog.Parsing;
using System;
using System.Collections.Generic;

namespace Scarlet.Serilog.Sinks.Graylog.Tests
{
    public class LogEventSource
    {
        public static LogEvent GetSimpleLogEvent(DateTimeOffset date)
        {
            var logEvent = new LogEvent(date, LogEventLevel.Information, null,
                new MessageTemplate("abcdef{TestProp}", new List<MessageTemplateToken>
                {
                    new TextToken("abcdef"),
                    new PropertyToken("TestProp", "zxc", alignment:new Alignment(AlignmentDirection.Left, 3))

                }), new List<LogEventProperty>
                {
                    new LogEventProperty("TestProp", new ScalarValue("zxc")),
                    new LogEventProperty("id", new ScalarValue("asd"))
                });
            return logEvent;
        }

        public static LogEvent GetErrorEvent(DateTimeOffset date)
        {
            var logEvent = new LogEvent(date, LogEventLevel.Information, new InvalidCastException("Some errror"),
                new MessageTemplate("", new List<MessageTemplateToken>()),
                new List<LogEventProperty>(new List<LogEventProperty>()));
            return logEvent;
        }

        public static LogEvent GetComplexEvent(DateTimeOffset date)
        {
            var logEvent = new LogEvent(date, LogEventLevel.Information, null,
                new MessageTemplate("abcdef{TestProp}", new List<MessageTemplateToken>
                {
                    new TextToken("abcdef"),
                    new PropertyToken("TestProp", "zxc", alignment:new Alignment(AlignmentDirection.Left, 3))

                }), new List<LogEventProperty>
                {
                    new LogEventProperty("TestProp", new ScalarValue("zxc")),
                    new LogEventProperty("id", new ScalarValue("asd")),
                    new LogEventProperty("StructuredProperty",
                        new StructureValue(new List<LogEventProperty>
                        {
                            new LogEventProperty("id", new ScalarValue(1)),
                            new LogEventProperty("_TestProp", new ScalarValue(3)),
                        }, "TypeTag"))
                });
            return logEvent;
        }

        /// <summary>
        /// A log event carrying exactly one named property, so a fixture can assert the JSON of a
        /// single GELF additional field. <paramref name="value"/> is wrapped verbatim in a
        /// <see cref="ScalarValue"/> - Serilog's capturing pipeline is deliberately bypassed so that
        /// types which it would normally pre-convert can still be exercised.
        /// </summary>
        public static LogEvent GetScalarEvent(string propertyName, object? value, DateTimeOffset? date = null)
        {
            return new LogEvent(date ?? DateTimeOffset.UnixEpoch, LogEventLevel.Information, null,
                new MessageTemplate("", new List<MessageTemplateToken>()),
                new List<LogEventProperty>
                {
                    new LogEventProperty(propertyName, new ScalarValue(value))
                });
        }

        /// <summary>
        /// A log event carrying one property built from an arbitrary <see cref="LogEventPropertyValue"/>,
        /// for the sequence/dictionary/structure branches of <c>AddAdditionalField</c>.
        /// </summary>
        public static LogEvent GetPropertyEvent(string propertyName, LogEventPropertyValue value, DateTimeOffset? date = null)
        {
            return new LogEvent(date ?? DateTimeOffset.UnixEpoch, LogEventLevel.Information, null,
                new MessageTemplate("", new List<MessageTemplateToken>()),
                new List<LogEventProperty>
                {
                    new LogEventProperty(propertyName, value)
                });
        }

        public static LogEvent GetExceptionLogEvent(DateTimeOffset date, Exception testExc)
        {
            var logevent = new LogEvent(date, LogEventLevel.Error, testExc, new MessageTemplate("", new List<MessageTemplateToken>()),
                new List<LogEventProperty>(new List<LogEventProperty>()));
            return logevent;
        }

        /// <summary>
        /// An exception chain <paramref name="depth"/> levels deep whose every link was really thrown
        /// and caught, so each one carries a stack trace. The outermost message is
        /// "Level {depth} exception" and the innermost "Level 1 exception".
        /// </summary>
        /// <remarks>
        /// <see cref="Core.MessageBuilders.ExceptionMessageBuilder"/> joins the messages of the chain
        /// and appends the stack trace of every link that has one - which excludes any exception that
        /// was constructed but never thrown.
        /// </remarks>
        public static Exception NestedException(int depth = 2)
        {
            if (depth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(depth), depth, "An exception chain needs at least one exception.");
            }

            Exception? caught = null;

            for (int level = 1; level <= depth; level++)
            {
                try
                {
                    throw caught == null
                        ? new InvalidOperationException($"Level {level} exception")
                        : new InvalidOperationException($"Level {level} exception", caught);
                }
                catch (Exception thrown)
                {
                    caught = thrown;
                }
            }

            return caught!;
        }
    }
}
