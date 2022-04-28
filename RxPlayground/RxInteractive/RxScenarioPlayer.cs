using System.Collections.Immutable;
using System.Reactive;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RxPlayground.RxInteractive
{
    public record RxInstructionWithLineNumber(
        RxInstruction Instruction,
        int LineNumber,
        int LineCount);

    public record RxScenario(
        ImmutableList<RxInstructionWithLineNumber> Instructions,
        string FullCode)
    {
        public static RxScenario Create(IEnumerable<RxInstruction> instructions)
        {
            var results = ImmutableList.CreateBuilder<RxInstructionWithLineNumber>();
            var fullCode = new StringBuilder();
            var lineNumber = 0;

            foreach (var instruction in instructions)
            {
                var lineCount = instruction.Code.Split("\n").Length;
                results.Add(new(instruction, lineNumber, lineCount));
                fullCode.AppendLine(instruction.Code);
                lineNumber += lineCount;
            }

            return new RxScenario(results.ToImmutable(), fullCode.ToString());
        }
    }

    public abstract record RxInstruction(string Code)
    {
        public record DeclareObservableInstruction(string Name, object Observable, string Code) : RxInstruction(Code);
        public record SubscribeInstruction(object Observable, object Observer, Action<IDisposable> OnSubscribed, string Code) : RxInstruction(Code);
        public record UnsubscribeInstruction(Func<IDisposable> GetSubscription, string Code) : RxInstruction(Code);

        public static RxInstruction DeclareObservable<T>(out T observableOutput, T observable,
            [CallerArgumentExpression("observableOutput")] string varNameExpression = "",
            [CallerArgumentExpression("observable")] string observableExpression = "")
            where T : notnull
        {
            observableOutput = observable;

            var name = varNameExpression.Replace("var ", "");
            var code = FixIndentAndRemoveVisualizeCalls(observableExpression);

            return new DeclareObservableInstruction(
                Name: name,
                Observable: observable,
                Code: $"var {name} = {code};");

            static string FixIndentAndRemoveVisualizeCalls(string code)
            {
                var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                    .Select(line => RemoveVisualizeCall(line))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

                var commonIndentation = (lines.Count == 1) ? 0 : lines.Skip(1).Min(line => line.TakeWhile(c => c == ' ').Count());

                return string.Join("\n", lines
                    .Skip(1)
                    .Select(line => "    " + line[commonIndentation..])
                    .Prepend(lines[0]));
            }

            static string RemoveVisualizeCall(string line) =>
                Regex.Replace(line, "\\.Visualize\\(.*", "");
        }

        public static RxInstruction Subscribe<T>(
            Action<IDisposable> onSubscribed,
            IObservable<T> observable,
            Action<T> onNext,
            [CallerArgumentExpression("onSubscribed")] string onSubscribedExpression = "",
            [CallerArgumentExpression("observable")] string observableVarName = "",
            [CallerArgumentExpression("onNext")] string onNextExpression = "")
        {
            var observer = Observer.Create(onNext, _ => { }, () => { });

            var subscriptionVarName = Regex.Replace(onSubscribedExpression, "^.*=>\\s*(\\w+)\\s*=\\s*.*$", "$1").Trim();

            return new SubscribeInstruction(
                observable,
                observer,
                onSubscribed,
                $"var {subscriptionVarName} = {observableVarName}.Subscribe({onNextExpression});");
        }

        public static RxInstruction Unsubscribe(Func<IDisposable> getSubscription, [CallerArgumentExpression("subscription")] string getSubscriptionExpression = "")
        {
            var subscriptionVarName = Regex.Replace(getSubscriptionExpression, "^.*=>\\s*", "");
            return new UnsubscribeInstruction(getSubscription, $"{subscriptionVarName}.Dispose();");
        }
    }

    public class RxScenarioPlayer : IDisposable
    {
        public record State(
            int InstructionPointer,
            RxInstructionWithLineNumber? NextInstruction);

        public RxScenario Scenario { get; }

        public RxInteractiveSession Session { get; }

        public IObservable<State> StateObservable => stateSubject;

        private readonly object stateLock = new();
        private readonly BehaviorSubject<State> stateSubject;

        public RxScenarioPlayer(RxScenario scenario, ILogger logger)
        {
            Scenario = scenario;
            Session = new RxInteractiveSession(SystemTimeProvider.Instance, logger);
            stateSubject = new(new(0, scenario.Instructions[0]));
        }

        public bool TryStep()
        {
            lock (stateLock)
            {
                var state = stateSubject.Value;

                if (state.InstructionPointer >= Scenario.Instructions.Count)
                    return false; // reached end of program

                var instruction = Scenario.Instructions[state.InstructionPointer];
                ExecuteInstruction(instruction.Instruction);

                var nextInstruction = (state.InstructionPointer + 1) < Scenario.Instructions.Count
                    ? Scenario.Instructions[state.InstructionPointer + 1]
                    : null;

                stateSubject.OnNext(state with
                {
                    InstructionPointer = state.InstructionPointer + 1,
                    NextInstruction = nextInstruction
                });

                return true;
            }
        }

        public void RunToEnd()
        {
            while (TryStep()) ;
        }

        private void ExecuteInstruction(RxInstruction instruction)
        {
            switch (instruction)
            {
                case RxInstruction.DeclareObservableInstruction instr:
                    Session.DeclareObservable(instr.Observable);
                    break;

                case RxInstruction.SubscribeInstruction instr2:
                    Session.DeclareSubscription(instr2.Observable, instr2.Observer, instr2.OnSubscribed);
                    break;

                case RxInstruction.UnsubscribeInstruction instr3:
                    instr3.GetSubscription().Dispose();
                    break;
            }
        }

        public void Dispose()
        {
            stateSubject.Dispose();
            Session.Dispose();
        }
    }
}
