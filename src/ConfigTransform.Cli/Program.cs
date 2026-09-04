using ConfigTransform.Cli;
using ConfigTransform.Core;

return CliRunner.Run(
    args, Console.Out, Console.Error, FormatEngines.All,
    workingDirectory: null, stdin: Console.In, interactiveAllowed: !Console.IsInputRedirected);
