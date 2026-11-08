using System.Text;
using Opti.Cli;

Console.OutputEncoding = Encoding.UTF8;
var a = new Args(args);

try
{
    return a.Command switch
    {
        "run" => await Commands.Run(a),
        "analyze" => Commands.Analyze(a),
        "plan" => Commands.Plan(a),
        "measure" => await Commands.Measure(a),
        "apply" => Commands.Apply(a),
        "compare" => Commands.Compare(a),
        "ab" => await Commands.Ab(a),
        "resume" => await Commands.Resume(a),
        "report" => Commands.Report(a),
        "rollback" => Commands.Rollback(a),
        "history" => Commands.History(a),
        "latency" => Commands.Latency(a),
        "net" => await Commands.Net(a),
        "version" or "--version" => await Commands.Version(a),
        "doctor" => Doctor.Run(a),
        _ => Commands.Help(),
    };
}
catch (Exception ex) when (ex is PlatformNotSupportedException or UnauthorizedAccessException or FileNotFoundException
                               or ArgumentException or InvalidOperationException or OperationCanceledException or TimeoutException)
{
    Output.Color(ConsoleColor.Red, "Ошибка: " + ex.Message + "\n");
    return 2;
}
