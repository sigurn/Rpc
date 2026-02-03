namespace Sigurn.Rpc.TestProcess;

[RemoteInterface]
public interface ITestProcess
{
    string ProcessPath { get; }

    int TestMathod(int number);

    void Exit();
}