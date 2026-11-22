using Catalyst.Commands;
using Catalyst.Common.Services;
using Spectre.Console.Cli;

PipedConsole.Configure();

CommandApp app = new();

app.Configure(config => AppConfiguration.Configure(config, args, ApplicationVersion.Current));

return app.Run(args);
