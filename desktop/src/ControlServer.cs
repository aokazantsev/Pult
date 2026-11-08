using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Pult
{
    internal sealed class ControlServer : IDisposable
    {
        private readonly object gate = new object();
        private readonly string pipeName;
        private readonly Func<ControlCommand, Dictionary<string, object>> handler;
        private NamedPipeServerStream waiting;
        private volatile bool stopped;

        public ControlServer(string pipeName, Func<ControlCommand, Dictionary<string, object>> handler)
        {
            this.pipeName = pipeName;
            this.handler = handler;
        }

        public bool Start()
        {
            NamedPipeServerStream first;
            try
            {
                first = Create();
            }
            catch (IOException error)
            {
                AppLog.Append("control channel not started: " + error.Message);
                return false;
            }
            catch (UnauthorizedAccessException error)
            {
                AppLog.Append("control channel not started: " + error.Message);
                return false;
            }
            if (!ControlChannel.OwnedByCurrentUser(first))
            {
                first.Dispose();
                AppLog.Append("control channel not started: the pipe name is held by another user");
                return false;
            }
            lock (gate) waiting = first;
            new Thread(Listen) { IsBackground = true, Name = "control" }.Start();
            AppLog.Append("control channel ready");
            return true;
        }

        public void Dispose()
        {
            stopped = true;
            lock (gate)
            {
                if (waiting != null) waiting.Dispose();
                waiting = null;
            }
        }

        private NamedPipeServerStream Create()
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, ControlChannel.Security());
        }

        private void Listen()
        {
            while (!stopped)
            {
                NamedPipeServerStream pipe;
                lock (gate)
                {
                    if (stopped) return;
                    pipe = waiting;
                }
                try
                {
                    if (pipe == null)
                    {
                        pipe = Create();
                        lock (gate)
                        {
                            if (stopped)
                            {
                                pipe.Dispose();
                                return;
                            }
                            waiting = pipe;
                        }
                    }
                    IAsyncResult connecting = pipe.BeginWaitForConnection(null, null);
                    connecting.AsyncWaitHandle.WaitOne();
                    pipe.EndWaitForConnection(connecting);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception error)
                {
                    if (stopped) return;
                    AppLog.Append("control channel: " + error.Message);
                    lock (gate)
                    {
                        if (waiting == pipe) waiting = null;
                    }
                    pipe.Dispose();
                    Thread.Sleep(1000);
                    continue;
                }
                lock (gate)
                {
                    if (waiting == pipe) waiting = null;
                }
                NamedPipeServerStream connected = pipe;
                new Thread(() => Serve(connected)) { IsBackground = true, Name = "control-request" }.Start();
            }
        }

        private void Serve(NamedPipeServerStream pipe)
        {
            ControlCommand command = null;
            using (pipe)
            {
                try
                {
                    string line = ControlChannel.ReadLine(pipe);
                    if (line == null) return;
                    Dictionary<string, object> answer = Answer(line, out command);
                    ControlChannel.WriteLine(pipe, Json.Write(answer));
                    pipe.WaitForPipeDrain();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (InvalidDataException error)
                {
                    AppLog.Append("control: request refused: " + error.Message);
                }
                catch (ArgumentException error)
                {
                    AppLog.Append("control: request refused: " + error.Message);
                }
            }
            if (command != null && command.AfterReply != null) command.AfterReply();
        }

        private Dictionary<string, object> Answer(string line, out ControlCommand command)
        {
            command = null;
            Dictionary<string, object> request = Json.TryParseObject(line);
            var args = new List<string>();
            var list = request == null ? null : Json.Value(request, "args") as IList;
            if (list != null)
            {
                foreach (object item in list)
                {
                    var text = item as string;
                    if (text == null) return ControlResult.Fail(ControlResult.Usage, "arguments must be strings");
                    args.Add(text);
                }
            }
            if (request == null) return ControlResult.Fail(ControlResult.Usage, "request is not json");
            string problem;
            command = ControlCommand.Parse(args, Json.String(request, "cwd"), out problem);
            if (command == null) return ControlResult.Fail(ControlResult.Usage, problem);
            if (command.IsLocal) return ControlResult.Fail(ControlResult.Usage, command.Name + " runs without the running Pult");
            AppLog.Append("control: " + command.Name);
            try
            {
                return handler(command);
            }
            catch (Exception error)
            {
                AppLog.Append("control: " + command.Name + " failed: " + error);
                return ControlResult.Fail(ControlResult.Failed, error.Message);
            }
        }
    }
}
