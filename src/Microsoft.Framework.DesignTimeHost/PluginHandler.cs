﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Framework.DesignTimeHost.Models.IncomingMessages;
using Microsoft.Framework.DesignTimeHost.Models.OutgoingMessages;
using Microsoft.Framework.Internal;
using Microsoft.Framework.Runtime;
using Microsoft.Framework.Runtime.Common.DependencyInjection;

namespace Microsoft.Framework.DesignTimeHost
{
    public class PluginHandler
    {
        private const string RegisterPluginMessageName = "RegisterPlugin";
        private const string UnregisterPluginMessageName = "UnregisterPlugin";
        private const string PluginMessageMessageName = "PluginMessage";

        private readonly Action<object> _sendMessageMethod;
        private readonly IServiceProvider _hostServices;
        private readonly Queue<PluginMessage> _faultedRegisterPluginMessages;
        private readonly Queue<PluginMessage> _messageQueue;
        private readonly IDictionary<string, IPlugin> _registeredPlugins;
        private readonly IDictionary<string, Assembly> _assemblyCache;
        private readonly IDictionary<string, Type> _pluginTypeCache;

        public PluginHandler([NotNull] IServiceProvider hostServices, [NotNull] Action<object> sendMessageMethod)
        {
            _hostServices = hostServices;
            _sendMessageMethod = sendMessageMethod;
            _messageQueue = new Queue<PluginMessage>();
            _faultedRegisterPluginMessages = new Queue<PluginMessage>();
            _registeredPlugins = new Dictionary<string, IPlugin>(StringComparer.Ordinal);
            _assemblyCache = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            _pluginTypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        }

        public bool FaultedPluginRegistrations
        {
            get
            {
                return _faultedRegisterPluginMessages.Count > 0;
            }
        }

        public PluginHandlerOnReceiveResult OnReceive([NotNull] PluginMessage message)
        {
            _messageQueue.Enqueue(message);

            if (message.MessageName == RegisterPluginMessageName)
            {
                return PluginHandlerOnReceiveResult.ResolveDependencies;
            }

            return PluginHandlerOnReceiveResult.Default;
        }

        public void TryRegisterFaultedPlugins([NotNull] IAssemblyLoadContext assemblyLoadContext)
        {
            // Capture count here so when we enqueue later on we don't result in an infinite loop below.
            var faultedCount = _faultedRegisterPluginMessages.Count;

            for (var i = faultedCount; i > 0; i--)
            {
                var faultedRegisterPluginMessage = _faultedRegisterPluginMessages.Dequeue();
                var response = RegisterPlugin(faultedRegisterPluginMessage, assemblyLoadContext);

                if (response.Success)
                {
                    SendMessage(faultedRegisterPluginMessage.PluginId, response);
                }
                else
                {
                    // We were unable to recover, re-enqueue the faulted register plugin message.
                    _faultedRegisterPluginMessages.Enqueue(faultedRegisterPluginMessage);
                }
            }
        }

        public void ProcessMessages([NotNull] IAssemblyLoadContext assemblyLoadContext)
        {
            while (_messageQueue.Count > 0)
            {
                var message = _messageQueue.Dequeue();

                switch (message.MessageName)
                {
                    case RegisterPluginMessageName:
                        RegisterMessage(message, assemblyLoadContext);
                        break;
                    case UnregisterPluginMessageName:
                        UnregisterMessage(message);
                        break;
                    case PluginMessageMessageName:
                        PluginMessage(message, assemblyLoadContext);
                        break;
                }
            }
        }

        private void RegisterMessage(PluginMessage message, IAssemblyLoadContext assemblyLoadContext)
        {
            var response = RegisterPlugin(message, assemblyLoadContext);

            if (!response.Success)
            {
                _faultedRegisterPluginMessages.Enqueue(message);
            }

            SendMessage(message.PluginId, response);
        }

        private void UnregisterMessage(PluginMessage message)
        {
            if (!_registeredPlugins.Remove(message.PluginId))
            {
                OnError(
                    message.PluginId,
                    UnregisterPluginMessageName,
                    errorMessage: Resources.FormatPlugin_UnregisteredPluginIdCannotUnregister(message.PluginId));
            }
            else
            {
                SendMessage(
                    message.PluginId,
                    new PluginResponseMessage
                    {
                        MessageName = UnregisterPluginMessageName,
                        Success = true
                    });
            }
        }

        private void PluginMessage(PluginMessage message, IAssemblyLoadContext assemblyLoadContext)
        {
            IPlugin plugin;
            if (_registeredPlugins.TryGetValue(message.PluginId, out plugin))
            {
                plugin.ProcessMessage(message.Data, assemblyLoadContext);
            }
            else
            {
                OnError(
                    message.PluginId,
                    PluginMessageMessageName,
                    errorMessage: Resources.FormatPlugin_UnregisteredPluginIdCannotReceiveMessages(message.PluginId));
            }
        }

        private PluginResponseMessage RegisterPlugin(
            PluginMessage message,
            IAssemblyLoadContext assemblyLoadContext)
        {
            var registerData = message.Data.ToObject<PluginRegisterData>();
            var response = new PluginResponseMessage
            {
                MessageName = RegisterPluginMessageName
            };

            var pluginId = message.PluginId;
            var registerDataTypeCacheKey = registerData.GetFullTypeCacheKey();
            IPlugin plugin;
            Type pluginType;

            if (!_pluginTypeCache.TryGetValue(registerDataTypeCacheKey, out pluginType))
            {
                try
                {
                    Assembly assembly;
                    if (!_assemblyCache.TryGetValue(registerData.AssemblyName, out assembly))
                    {
                        assembly = assemblyLoadContext.Load(registerData.AssemblyName);
                    }

                    pluginType = assembly.GetType(registerData.TypeName);
                }
                catch (Exception exception)
                {
                    response.Error = exception.Message;

                    return response;
                }
            }

            if (pluginType == null)
            {
                response.Error = Resources.FormatPlugin_TypeCouldNotBeLocatedInAssembly(
                    pluginId,
                    registerData.TypeName,
                    registerData.AssemblyName);

                return response;
            }
            else
            {
                // We build out a custom plugin service provider to add a PluginMessageBroker and 
                // IAssemblyLoadContext to the potential services that can be used to construct an IPlugin.
                var pluginServiceProvider = new PluginServiceProvider(
                    _hostServices,
                    messageBroker: new PluginMessageBroker(pluginId, _sendMessageMethod));

                plugin = ActivatorUtilities.CreateInstance(pluginServiceProvider, pluginType) as IPlugin;

                if (plugin == null)
                {
                    response.Error = Resources.FormatPlugin_CannotProcessMessageInvalidPluginType(
                        pluginId,
                        pluginType.FullName,
                        typeof(IPlugin).FullName);

                    return response;
                }
            }

            Debug.Assert(plugin != null);

            _registeredPlugins[pluginId] = plugin;

            response.Success = true;

            return response;
        }

        private void SendMessage(string pluginId, PluginResponseMessage message)
        {
            var messageBroker = new PluginMessageBroker(pluginId, _sendMessageMethod);

            messageBroker.SendMessage(message);
        }

        private void OnError(string pluginId, string messageName, string errorMessage)
        {
            SendMessage(
                pluginId,
                message: new PluginResponseMessage
                {
                    MessageName = messageName,
                    Error = errorMessage,
                    Success = false
                });
        }

        private class PluginServiceProvider : IServiceProvider
        {
            private static readonly TypeInfo MessageBrokerTypeInfo = typeof(IPluginMessageBroker).GetTypeInfo();
            private readonly IServiceProvider _hostServices;
            private readonly PluginMessageBroker _messageBroker;

            public PluginServiceProvider(
                IServiceProvider hostServices,
                PluginMessageBroker messageBroker)
            {
                _hostServices = hostServices;
                _messageBroker = messageBroker;
            }

            public object GetService(Type serviceType)
            {
                var hostProvidedService = _hostServices.GetService(serviceType);

                if (hostProvidedService == null)
                {
                    var serviceTypeInfo = serviceType.GetTypeInfo();

                    if (MessageBrokerTypeInfo.IsAssignableFrom(serviceTypeInfo))
                    {
                        return _messageBroker;
                    }
                }

                return hostProvidedService;
            }
        }

        private class PluginRegisterData
        {
            public string AssemblyName { get; set; }
            public string TypeName { get; set; }

            public string GetFullTypeCacheKey()
            {
                return $"{TypeName}, {AssemblyName}";
            }
        }
    }
}