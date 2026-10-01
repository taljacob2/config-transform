using ConfigTransform.Cli;
using ConfigTransform.Core;

return Utf8Console.Run((stdout, stderr) => CliRunner.Run(
    args, stdout, stderr, FormatEngines.All,
    workingDirectory: null, stdin: Console.In, interactiveAllowed: !Console.IsInputRedirected));
