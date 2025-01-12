﻿using CommandLine;
using LogSetup;
using Lokql.Engine;
using NLog;

LoggingExtensions.SetupLoggingForConsole(LogLevel.Info);
await StandardParsers.Default
    .ParseArguments(args,
        typeof(CmdExplore.Options),
        typeof(CmdRun.Options),
        typeof(CmdTest.Options))
    .WithParsedAsync<CmdExplore.Options>(CmdExplore.RunAsync)
    .WithParsedAsync<CmdRun.Options>(CmdRun.RunAsync)
    .WithParsedAsync<CmdTest.Options>(CmdTest.RunAsync)

    ;
