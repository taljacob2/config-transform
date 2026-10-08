using ConfigTransform.Cli;
using ConfigTransform.Core;
using ConfigTransform.Secrets.AzureKeyVault;

// --color auto (the default): colour diffs only on a real terminal, and never when NO_COLOR is set
// to anything non-empty (https://no-color.org). --color always/never override both.
var autoColor = !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

return Utf8Console.Run((stdout, stderr) => CliRunner.Run(
    args, stdout, stderr, FormatEngines.All,
    workingDirectory: null, stdin: Console.In, interactiveAllowed: !Console.IsInputRedirected,
    autoColor: autoColor, keyVault: new AzureKeyVault()));
