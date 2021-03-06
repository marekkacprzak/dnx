﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Framework.DesignTimeHost.Models.IncomingMessages;
using Microsoft.Framework.DesignTimeHost.Models.OutgoingMessages;
using Microsoft.Framework.Runtime;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Framework.DesignTimeHost
{
    public class PluginHandlerFacts
    {
        private const string RandomGuidId = "d81b8ad8-306d-474b-b8a9-b25c7f80be7e";

        [Fact]
        public void TryRegisterFaultedPlugins_RecoversFaultedPluginRegistrations()
        {
            var pluginType = typeof(MessageBrokerCreationTestPlugin);
            var typeNameLookups = new Dictionary<string, Type>
            {
                { pluginType.FullName, pluginType }
            };
            var testAssembly = new TestAssembly(typeNameLookups);
            var assemblyLookups = new Dictionary<string, Assembly>
            {
                { "CustomAssembly", testAssembly }
            };
            var assemblyLoadContext = new FailureAssemblyLoadContext(assemblyLookups);
            var creationChecker = new PluginTypeCreationChecker();
            var serviceLookups = new Dictionary<Type, object>
            {
                { typeof(PluginTypeCreationChecker), creationChecker }
            };
            var serviceProvider = new TestServiceProvider(serviceLookups);
            object rawMessageBrokerData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawMessageBrokerData = data);
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(MessageBrokerCreationTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawMessageBrokerData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.False(responseMessage.Success);
            Assert.Equal("RegisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.NotEmpty(responseMessage.Error);
            Assert.False(creationChecker.Created);

            pluginHandler.TryRegisterFaultedPlugins(assemblyLoadContext);

            messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawMessageBrokerData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.True(responseMessage.Success);
            Assert.Equal("RegisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Null(responseMessage.Error);
            Assert.True(creationChecker.Created);
        }

        [Fact]
        public void ProcessMessages_NoOpsWithoutMessagesEnqueued()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<TestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawMessageBrokerData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawMessageBrokerData = data);

            pluginHandler.ProcessMessages(assemblyLoadContext);

            Assert.Null(rawMessageBrokerData);
        }

        [Fact]
        public void TryRegisterFaultedPlugins_NoOpsWithoutMessagesEnqueued()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<CreationTestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawMessageBrokerData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawMessageBrokerData = data);

            pluginHandler.TryRegisterFaultedPlugins(assemblyLoadContext);

            Assert.Null(rawMessageBrokerData);
        }

        [Fact]
        public void OnReceive_DoesNotProcessMessages()
        {
            object rawMessageBrokerData = null;
            var serviceProvider = new TestServiceProvider();
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawMessageBrokerData = data);
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(TestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var unregisterPluginMessage = new PluginMessage
            {
                MessageName = "UnregisterPlugin",
                PluginId = RandomGuidId
            };
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "Data", "Hello Plugin" },
                },
                MessageName = "PluginMessage",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.OnReceive(unregisterPluginMessage);

            Assert.Null(rawMessageBrokerData);
        }

        [Fact]
        public void ProcessMessages_RegisterPlugin_CreatesPlugin()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<CreationTestPlugin>();
            var creationChecker = new PluginTypeCreationChecker();
            var serviceLookups = new Dictionary<Type, object>
            {
                { typeof(PluginTypeCreationChecker), creationChecker }
            };
            var serviceProvider = new TestServiceProvider(serviceLookups);
            var pluginHandler = new PluginHandler(serviceProvider, (_) => { });
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(CreationTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            Assert.True(creationChecker.Created);
        }

        [Fact]
        public void ProcessMessages_RegisterPlugin_DoesNotCacheCreatedPlugin()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<CreationTestPlugin>();
            var creationChecker = new PluginTypeCreationChecker();
            var serviceLookups = new Dictionary<Type, object>
            {
                { typeof(PluginTypeCreationChecker), creationChecker }
            };
            var serviceProvider = new TestServiceProvider(serviceLookups);
            var pluginHandler = new PluginHandler(serviceProvider, (_) => { });
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(CreationTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);
            Assert.True(creationChecker.Created);
            creationChecker.Created = false;
            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);
            Assert.True(creationChecker.Created);
        }

        [Fact]
        public void ProcessMessages_RegisterPlugin_CreatesPluginWithDefaultMessageBroker()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<MessageBrokerCreationTestPlugin>();
            var creationChecker = new PluginTypeCreationChecker();
            var serviceLookups = new Dictionary<Type, object>
            {
                { typeof(PluginTypeCreationChecker), creationChecker }
            };
            var serviceProvider = new TestServiceProvider(serviceLookups);
            object rawMessageBrokerData = null;
            var pluginHandler = new PluginHandler(
                serviceProvider, (data) => rawMessageBrokerData = data);
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(MessageBrokerCreationTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            Assert.True(creationChecker.Created);
            Assert.NotNull(rawMessageBrokerData);
            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawMessageBrokerData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.True(responseMessage.Success);
            Assert.Equal("RegisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Null(responseMessage.Error);
        }

        [Fact]
        public void ProcessMessages_RegisterPlugin_CreatesPluginWithCustomMessageBroker()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<MessageBrokerCreationTestPlugin>();
            var creationChecker = new PluginTypeCreationChecker();
            object rawMessageBrokerData = null;
            var pluginMessageBroker = new PluginMessageBroker(RandomGuidId, (data) => rawMessageBrokerData = data);
            var serviceLookups = new Dictionary<Type, object>
            {
                { typeof(PluginTypeCreationChecker), creationChecker },
                { typeof(IPluginMessageBroker),  pluginMessageBroker }
            };
            var serviceProvider = new TestServiceProvider(serviceLookups);
            var pluginHandler = new PluginHandler(serviceProvider, (_) => { });
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(MessageBrokerCreationTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "Data", "Hello Plugin" },
                },
                MessageName = "PluginMessage",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            Assert.True(creationChecker.Created);
            Assert.NotNull(rawMessageBrokerData);
            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawMessageBrokerData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            Assert.Equal("Created", messageBrokerData.Data.ToString(), StringComparer.Ordinal);
        }

        [Fact]
        public void ProcessMessages_RegisterPlugin_CreatesPluginWithAssemblyLoadContext()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<AssemblyLoadContextRelayTestPlugin>();
            var serviceLookups = new Dictionary<Type, object>();
            var serviceProvider = new TestServiceProvider(serviceLookups);
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(
                serviceProvider, (data) => rawWrappedData = data);
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(AssemblyLoadContextRelayTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "Data", "Hello Plugin" },
                },
                MessageName = "PluginMessage",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            Assert.NotNull(rawWrappedData);
            var wrappedData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, wrappedData.PluginId);
            Assert.Same(assemblyLoadContext, wrappedData.Data);
        }

        [Fact]
        public void ProcessMessages_RegisterPlugin_SendsErrorWhenUnableToLocatePluginType()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<TestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawWrappedData = data);
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(TestPlugin).FullName + "_" },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var expectedErrorMessage =
                $"Could not locate plugin id '{RandomGuidId}' of type '{typeof(TestPlugin).FullName + "_"}' " +
                "in assembly 'CustomAssembly'.";

            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.False(responseMessage.Success);
            Assert.Equal("RegisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Equal(expectedErrorMessage, responseMessage.Error, StringComparer.Ordinal);
        }

        [Fact]
        public void ProcessMessages_RegisterPlugin_SendsErrorOnInvalidPluginTypes()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<InvalidTestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawWrappedData = data);
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(InvalidTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var expectedErrorMessage =
                $"Cannot process plugin message. Plugin id '{RandomGuidId}' of type " +
                "'Microsoft.Framework.DesignTimeHost.PluginHandlerFacts+InvalidTestPlugin' must be assignable " +
                "to type 'Microsoft.Framework.DesignTimeHost.IPlugin'.";

            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.False(responseMessage.Success);
            Assert.Equal("RegisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Equal(expectedErrorMessage, responseMessage.Error, StringComparer.Ordinal);
        }

        [Fact]
        public void ProcessMessages_UnregisterPlugin_UnregistersPlugin()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<TestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawWrappedData = data);
            var expectedErrorMessage =
                $"Message received for unregistered plugin id '{RandomGuidId}'. Plugins must first be registered " +
                "before they can receive messages.";
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(TestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var unregisterPluginMessage = new PluginMessage
            {
                MessageName = "UnregisterPlugin",
                PluginId = RandomGuidId
            };
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "Data", "Hello Plugin" },
                },
                MessageName = "PluginMessage",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.OnReceive(unregisterPluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.True(responseMessage.Success);
            Assert.Equal("UnregisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Null(responseMessage.Error);
        }

        [Fact]
        public void ProcessMessages_UnregisterPlugin_SendsErrorWhenUnregisteringUnknownPlugin()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<TestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawWrappedData = data);
            var unregisterPluginMessage = new PluginMessage
            {
                MessageName = "UnregisterPlugin",
                PluginId = RandomGuidId
            };
            var expectedErrorMessage =
                $"No plugin with id '{RandomGuidId}' has been registered. Cannot unregister plugin.";

            pluginHandler.OnReceive(unregisterPluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.False(responseMessage.Success);
            Assert.Equal("UnregisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Equal(expectedErrorMessage, responseMessage.Error, StringComparer.Ordinal);
        }

        [Fact]
        public void ProcessMessages_UnregisterPlugin_SendsErrorWhenUnregisteringPluginMoreThanOnce()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<TestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawWrappedData = data);
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(TestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var unregisterPluginMessage = new PluginMessage
            {
                MessageName = "UnregisterPlugin",
                PluginId = RandomGuidId
            };
            var expectedErrorMessage =
                $"No plugin with id '{RandomGuidId}' has been registered. Cannot unregister plugin.";

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.OnReceive(unregisterPluginMessage);
            pluginHandler.OnReceive(unregisterPluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.False(responseMessage.Success);
            Assert.Equal("UnregisterPlugin", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Equal(expectedErrorMessage, responseMessage.Error, StringComparer.Ordinal);
        }

        [Fact]
        public void ProcessMessages_PluginMessage_ProcessesMessages()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<MessageTestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawMessageBrokerData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawMessageBrokerData = data);
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(MessageTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "Data", "Hello Plugin" },
                },
                MessageName = "PluginMessage",
                PluginId = RandomGuidId
            };

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            Assert.NotNull(rawMessageBrokerData);
            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawMessageBrokerData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var actualMessage = (string)messageBrokerData.Data;
            Assert.Equal("Hello Plugin!", actualMessage, StringComparer.Ordinal);
        }

        [Fact]
        public void ProcessMessages_PluginMessage_SendsErrorWhenPluginNotRegistered()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<TestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawWrappedData = data);
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "Data", "Hello Plugin" },
                },
                MessageName = "PluginMessage",
                PluginId = RandomGuidId
            };
            var expectedErrorMessage =
                $"Message received for unregistered plugin id '{RandomGuidId}'. Plugins must first be registered " +
                "before they can receive messages.";

            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.False(responseMessage.Success);
            Assert.Equal("PluginMessage", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Equal(expectedErrorMessage, responseMessage.Error, StringComparer.Ordinal);
        }

        [Fact]
        public void ProcessMessages_PluginMessage_SendsErrorWhenUnregistered()
        {
            var assemblyLoadContext = CreateTestAssemblyLoadContext<MessageTestPlugin>();
            var serviceProvider = new TestServiceProvider();
            object rawWrappedData = null;
            var pluginHandler = new PluginHandler(serviceProvider, (data) => rawWrappedData = data);
            var registerPluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "AssemblyName", "CustomAssembly" },
                    { "TypeName", typeof(MessageTestPlugin).FullName },
                },
                MessageName = "RegisterPlugin",
                PluginId = RandomGuidId
            };
            var unregisterPluginMessage = new PluginMessage
            {
                MessageName = "UnregisterPlugin",
                PluginId = RandomGuidId
            };
            var pluginMessage = new PluginMessage
            {
                Data = new JObject
                {
                    { "Data", "Hello Plugin" },
                },
                MessageName = "PluginMessage",
                PluginId = RandomGuidId
            };
            var expectedErrorMessage =
                $"Message received for unregistered plugin id '{RandomGuidId}'. Plugins must first be registered " +
                "before they can receive messages.";

            pluginHandler.OnReceive(registerPluginMessage);
            pluginHandler.OnReceive(unregisterPluginMessage);
            pluginHandler.OnReceive(pluginMessage);
            pluginHandler.ProcessMessages(assemblyLoadContext);

            var messageBrokerData = Assert.IsType<PluginMessageBroker.PluginMessageWrapperData>(rawWrappedData);
            Assert.Equal(RandomGuidId, messageBrokerData.PluginId);
            var responseMessage = Assert.IsType<PluginResponseMessage>(messageBrokerData.Data);
            Assert.False(responseMessage.Success);
            Assert.Equal("PluginMessage", responseMessage.MessageName, StringComparer.Ordinal);
            Assert.Equal(expectedErrorMessage, responseMessage.Error, StringComparer.Ordinal);
        }

        private static IAssemblyLoadContext CreateTestAssemblyLoadContext<TPlugin>()
        {
            var pluginType = typeof(TPlugin);
            var typeNameLookups = new Dictionary<string, Type>
            {
                { pluginType.FullName, pluginType }
            };
            var testAssembly = new TestAssembly(typeNameLookups);
            var assemblyLookups = new Dictionary<string, Assembly>
            {
                { "CustomAssembly", testAssembly }
            };

            return new TestAssemblyLoadContext(assemblyLookups);
        }

        private class MessageTestPlugin : IPlugin
        {
            private readonly IPluginMessageBroker _messageBroker;

            public MessageTestPlugin(IPluginMessageBroker messageBroker)
            {
                _messageBroker = messageBroker;
            }

            public void ProcessMessage(JObject data, IAssemblyLoadContext assemblyLoadContext)
            {
                _messageBroker.SendMessage(data["Data"].ToString() + "!");
            }
        }

        private class TestPlugin : IPlugin
        {
            public virtual void ProcessMessage(JObject data, IAssemblyLoadContext assemblyLoadContext)
            {
            }
        }

        private class InvalidTestPlugin
        {
        }

        private class CreationTestPlugin : IPlugin
        {
            public CreationTestPlugin(PluginTypeCreationChecker creationChecker)
            {
                creationChecker.Created = true;
            }

            public virtual void ProcessMessage(JObject data, IAssemblyLoadContext assemblyLoadContext)
            {
                throw new NotImplementedException();
            }
        }

        private class MessageBrokerCreationTestPlugin : CreationTestPlugin
        {
            private readonly IPluginMessageBroker _messageBroker;

            public MessageBrokerCreationTestPlugin(
                IPluginMessageBroker messageBroker,
                PluginTypeCreationChecker creationChecker)
                : base(creationChecker)
            {
                _messageBroker = messageBroker;
            }

            public override void ProcessMessage(JObject data, IAssemblyLoadContext assemblyLoadContext)
            {
                _messageBroker.SendMessage("Created");
            }
        }

        private class AssemblyLoadContextRelayTestPlugin : TestPlugin
        {
            private readonly IPluginMessageBroker _messageBroker;

            public AssemblyLoadContextRelayTestPlugin(
                IPluginMessageBroker messageBroker)
            {
                _messageBroker = messageBroker;
            }

            public override void ProcessMessage(JObject data, IAssemblyLoadContext assemblyLoadContext)
            {
                _messageBroker.SendMessage(assemblyLoadContext);
            }
        }

        private class PluginTypeCreationChecker
        {
            public bool Created { get; set; }
        }

        private class FailureAssemblyLoadContext : TestAssemblyLoadContext
        {
            public bool _firstLoad;

            public FailureAssemblyLoadContext(IReadOnlyDictionary<string, Assembly> assemblyNameLookups)
                : base(assemblyNameLookups)
            {
            }

            public override Assembly Load(string name)
            {
                if (!_firstLoad)
                {
                    _firstLoad = true;

                    throw new InvalidOperationException();
                }

                return base.Load(name);
            }
        }
    }
}