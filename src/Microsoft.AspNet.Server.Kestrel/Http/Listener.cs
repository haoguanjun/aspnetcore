// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel.Networking;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    /// <summary>
    /// Base class for listeners in Kestrel. Listens for incoming connections
    /// </summary>
    public abstract class Listener : ListenerContext, IDisposable
    {
        protected UvStreamHandle ListenSocket { get; private set; }

        protected static void ConnectionCallback(UvStreamHandle stream, int status, Exception error, object state)
        {
            if (error != null)
            {
                Trace.WriteLine("Listener.ConnectionCallback " + error.ToString());
            }
            else
            {
                ((Listener)state).OnConnection(stream, status);
            }
        }

        protected Listener(IMemoryPool memory)
        {
            Memory = memory;
        }

        public Task StartAsync(
            string scheme,
            string host,
            int port,
            KestrelThread thread,
            Func<Frame, Task> application)
        {
            Thread = thread;
            Application = application;

            var tcs = new TaskCompletionSource<int>();
            Thread.Post(_ =>
            {
                try
                {
                    ListenSocket = CreateListenSocket(host, port);
                    tcs.SetResult(0);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        /// <summary>
        /// Creates the socket used to listen for incoming connections
        /// </summary>
        protected abstract UvStreamHandle CreateListenSocket(string host, int port);

        /// <summary>
        /// Handles an incoming connection
        /// </summary>
        /// <param name="listenSocket">Socket being used to listen on</param>
        /// <param name="status">Connection status</param>
        protected abstract void OnConnection(UvStreamHandle listenSocket, int status);

        protected virtual void DispatchConnection(UvStreamHandle socket)
        {
            var connection = new Connection(this, socket);
            connection.Start();
        }

        public void Dispose()
        {
            // Ensure the event loop is still running.
            // If the event loop isn't running and we try to wait on this Post
            // to complete, then KestrelEngine will never be disposed and
            // the exception that stopped the event loop will never be surfaced.
            if (Thread.FatalError == null)
            {
                var tcs = new TaskCompletionSource<int>();
                Thread.Post(
                    _ =>
                    {
                        try
                        {
                            ListenSocket.Dispose();
                            tcs.SetResult(0);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    },
                    null);

                // REVIEW: Should we add a timeout here to be safe?
                tcs.Task.Wait();
            }

            ListenSocket = null;
        }
    }
}
