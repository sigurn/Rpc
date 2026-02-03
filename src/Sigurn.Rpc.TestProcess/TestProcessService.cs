using System.Reflection;

namespace Sigurn.Rpc.TestProcess;

class TestProcessService : ITestProcess
{
    private readonly Action _exitHandler;
    public TestProcessService(Action exitHandler)
    {
        _exitHandler = exitHandler ?? throw new ArgumentNullException(nameof(exitHandler));
    }

    public string ProcessPath => Assembly.GetEntryAssembly()?.Location ?? string.Empty;

    public void Exit()
    {
        _exitHandler();
    }

    public int TestMathod(int number)
    {
        return number * 3;
    }
}