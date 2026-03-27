/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Cloud
{
    public class ConsoleTelemetry : ITelemetryContext, IDisposable
    {
        public ConsoleTelemetry(Action<ILoggingBuilder> configure = null)
        {
            string logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            string logFilePath = Path.Combine(logDirectory, "uacloudcommander.logfile.txt");

            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#else
                .MinimumLevel.Information()
#endif
                .WriteTo.Console()
                .WriteTo.File(
                    logFilePath,
                    fileSizeLimitBytes: 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 10)
                .CreateLogger();

            Log.Logger.Information("Log file is: {LogFilePath}", logFilePath);

            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddSerilog(Log.Logger, dispose: false);
                configure?.Invoke(builder);
            });

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += Unobserved_TaskException;
        }

        public ILoggerFactory LoggerFactory { get; internal set; }

        private readonly Lazy<Meter> _meter = new(() => new Meter("UA cloud app", "1.0.0"));
        public Meter CreateMeter() => _meter.Value;

        public ActivitySource ActivitySource { get; } = new("UA cloud app", "1.0.0");

        public void Dispose()
        {
            if (_meter.IsValueCreated)
            {
                _meter.Value.Dispose();
            }

            ActivitySource.Dispose();
            LoggerFactory?.Dispose();
            Log.CloseAndFlush();

            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= Unobserved_TaskException;
        }

        private void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs args)
        {
            Log.Logger.Error(
                args.ExceptionObject as Exception,
                "Unhandled Exception: (IsTerminating: {IsTerminating})",
                args.IsTerminating);
        }

        private void Unobserved_TaskException(
            object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            Log.Logger.Error(
                args.Exception,
                "Unobserved Task Exception (Observed: {Observed})",
                args.Observed);
        }
    }
}
