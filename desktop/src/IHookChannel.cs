namespace Pult
{
    internal interface IHookChannel
    {
        bool IsGone { get; }

        HookAck Deliver(string output);
    }
}
