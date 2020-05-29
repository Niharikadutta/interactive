﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;
using Microsoft.DotNet.Interactive.Server;

namespace Microsoft.DotNet.Interactive
{
    public class NamedPipeKernel : ProxyKernel
    {
        private NamedPipeClientStream _clientStream;
        private TextReader _reader;
        private TextWriter _writer;

        public NamedPipeKernel(string name) : base(name)
        {
            var command = new Command("#!named-pipe", "Connect to the specified named-pipe.")
            {
                new Argument<string>("pipe-name")
            };

            command.Handler = CommandHandler.Create<string, KernelInvocationContext>(async (pipeName, context) =>
            {
                await ConnectAsync(pipeName);
            });

            AddDirective(command);
        }

        private async Task PollEvents()
        {
            do
            {
                var line = await _reader.ReadLineAsync();
                var kernelEvent = KernelEventEnvelope.Deserialize(line).Event;
                PublishEvent(kernelEvent);
                if (kernelEvent is CommandHandled || kernelEvent is CommandFailed)
                {
                    break;
                }
            } while (true);
        }

        protected async override Task HandleRequestCompletion(RequestCompletion command, KernelInvocationContext context)
        {
            await _writer.WriteLineAsync(KernelCommandEnvelope.Serialize(command));
            await _writer.FlushAsync();
            await PollEvents();
        }

        protected async override Task HandleSubmitCode(SubmitCode command, KernelInvocationContext context)
        {
            _writer.WriteLine(KernelCommandEnvelope.Serialize(command));
            await _writer.FlushAsync();
            await PollEvents();
        }

        public async Task ConnectAsync(string pipeName)
        {
            if (_clientStream != null)
            {
                _clientStream.Close();
            }

            var clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            await clientStream.ConnectAsync();
            clientStream.ReadMode = PipeTransmissionMode.Message;
            _clientStream = clientStream;
            _reader = new StreamReader(clientStream);
            _writer = new StreamWriter(clientStream);
            RegisterForDisposal(clientStream);
        }
    }
}