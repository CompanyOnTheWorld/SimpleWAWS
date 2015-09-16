﻿using Serilog;
using SimpleWAWS.Code;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Web;

namespace SimpleWAWS.Trace
{
    public static class SimpleTrace
    {
        public static ILogger Analytics;
        public static ILogger Diagnostics;

        public static void TraceInformation(string message)
        {
            System.Diagnostics.Trace.TraceInformation(message);
        }

        public static void TraceInformation(string format, params string[] args)
        {
            if (args.Length > 0)
            {
                args[0] = string.Concat(args[0], "#", ExperimentManager.GetCurrentExperiment(), "$", ExperimentManager.GetCurrentSourceVariation(), "%", CultureInfo.CurrentCulture.EnglishName);
            }
            var cleaned = args.Select(e => e.Replace(";", "&semi"));

            System.Diagnostics.Trace.TraceInformation(format, cleaned);
        }

        public static void TraceError(string message)
        {
            System.Diagnostics.Trace.TraceError(message);
        }

        public static void TraceError(string format, params string[] args)
        {
            if (args.Length > 0)
            {
                args[0] = string.Concat(args[0], "#", ExperimentManager.GetCurrentExperiment(), "$", ExperimentManager.GetCurrentSourceVariation(), "%", CultureInfo.CurrentCulture.EnglishName);
            }

            System.Diagnostics.Trace.TraceError(format, args);
        }

        public static void TraceWarning(string message)
        {
            System.Diagnostics.Trace.TraceWarning(message);
        }

        public static void TraceWarning(string format, params string[] args)
        {
            if (args.Length > 0)
            {
                args[0] = string.Concat(args[0], "#", ExperimentManager.GetCurrentExperiment(), "$", ExperimentManager.GetCurrentSourceVariation(), "%", CultureInfo.CurrentCulture.EnglishName);
            }

            System.Diagnostics.Trace.TraceWarning(format, args);
        }
    }
}
