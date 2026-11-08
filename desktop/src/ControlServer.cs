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
        private readonly Func<PipeStream, bool> ownedByUser;
        private NamedPipeServerStream waiting;
        private volatile bool stopped;

        public ControlServer(string pipeName, Func<ControlCommand, Dictionary<string, object>> handler)
            : this(pipeName, handler, ControlChannel.OwnedByCurrentUser)
        {
        }

        public ControlServer(string pipeName, Func<ControlCommand, Dictionary<string, object>> handler, Func<PipeStream, bool> ownedByUser)
        {
            this.ownedByUser = ownedByUser;
            this.pipeName = pipeName;
            this.handler = handler;
        }

        public bool Start()
        {
            bool foreign;
            NamedPipeServerStream first = CreateOwned(out foreign, true);
            if (first == null) return false;
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

        public Func<NamedPipeServerStream> CreateInstance;

        public NamedPipeServerStream Create()
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, ControlChannel.Security());
        }

        private NamedPipeServerStream CreateOwned(out bool foreign, bool report)
        {
            foreign = false;
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreateInstance != null ? CreateInstance() : Create();
            }
            catch (IOException error)
            {
                if (report) AppLog.Append("control channel: instance not created: " + error.Message);
                return null;
            }
            catch (UnauthorizedAccessException error)
            {
                if (report) AppLog.Append("control channel: instance not created: " + error.Message);
                return null;
            }
            if (ownedByUser(pipe)) return pipe;
            pipe.Dispose();
            foreign = true;
            AppLog.Append("control channel stopped: the pipe name is held by another user");
            return null;
        }

        private void Listen()
        {
            while (true)
            {
                NamedPipeServerStream pipe;
                lock (gate)
                {
                    if (stopped || waiting == null) return;
                    pipe = waiting;
                }
                bool connected;
                try
                {
                    IAsyncResult connecting = pipe.BeginWaitForConnection(null, null);
                    connecting.AsyncWaitHandle.WaitOne();
                    pipe.EndWaitForConnection(connecting);
                    connected = true;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception error)
                {
                    if (stopped) return;
                    AppLog.Append("control channel: " + error.Message);
                    connected = false;
                }
                bool foreign = false;
                NamedPipeServerStream next = stopped ? null : CreateOwned(out foreign, true);
                Keep(ref next);
                if (connected) new Thread(() => Serve(pipe)) { IsBackground = true, Name = "control-request" }.Start();
                else pipe.Dispose();
                while (next == null && !foreign && !stopped)
                {
                    Thread.Sleep(1000);
                    next = CreateOwned(out foreign, false);
                    Keep(ref next);
                }
                if (next == null) return;
                if (!connected) Thread.Sleep(1000);
            }
        }

        private void Keep(ref NamedPipeServerStream next)
        {
            lock (gate)
            {
                if (stopped && next != null)
                {
                    next.Dispose();
                    next = null;
                }
                waiting = next;
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
            try
            {
                command = ControlCommand.Parse(args, Json.String(request, "cwd"), out problem);
                if (command == null) return ControlResult.Fail(ControlResult.Usage, problem);
                if (command.IsLocal) return ControlResult.Fail(ControlResult.Usage, command.Name + " runs without the running Pult");
                AppLog.Append("control: " + command.Name);
                return handler(command);
            }
            catch (Exception error)
            {
                AppLog.Append("control: " + (command == null ? "request" : command.Name) + " failed: " + error);
                return ControlResult.Fail(ControlResult.Failed, error.Message);
            }
        }
    }
}
