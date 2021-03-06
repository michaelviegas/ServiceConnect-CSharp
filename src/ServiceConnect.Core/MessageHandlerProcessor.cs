﻿//Copyright (C) 2015  Timothy Watson, Jakub Pachansky

//This program is free software; you can redistribute it and/or
//modify it under the terms of the GNU General Public License
//as published by the Free Software Foundation; either version 2
//of the License, or (at your option) any later version.

//This program is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//GNU General Public License for more details.

//You should have received a copy of the GNU General Public License
//along with this program; if not, write to the Free Software
//Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Common.Logging;
using Newtonsoft.Json;
using ServiceConnect.Interfaces;

namespace ServiceConnect.Core
{
    public class MessageHandlerProcessor : IMessageHandlerProcessor
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(MessageHandlerProcessor));

        private readonly IBusContainer _container;

        public MessageHandlerProcessor(IBusContainer container)
        {
            _container = container;
        }

        public void ProcessMessage<T>(string message, IConsumeContext context) where T : Message
        {
            IEnumerable<HandlerReference> handlerReferences = _container.GetHandlerTypes(typeof(IMessageHandler<T>))
                                                                        .Where(h => h.HandlerType.GetTypeInfo().BaseType == null || 
                                                                                    h.HandlerType.GetTypeInfo().BaseType.Name != typeof(ProcessManager<>).Name);
            InitHandlers<T>(message, context, handlerReferences);
        }

        private void ProcessMessageBaseType<T, TB>(string message, IConsumeContext context) where T : Message where TB : Message
        {
            IEnumerable<HandlerReference> handlerReferences = _container.GetHandlerTypes(typeof(IMessageHandler<TB>))
                                                                        .Where(h => h.HandlerType.GetTypeInfo().BaseType == null ||
                                                                                    h.HandlerType.GetTypeInfo().BaseType.Name != typeof(ProcessManager<>).Name);

            InitHandlers<T>(message, context, handlerReferences, typeof(TB));
        }        
        
        private void InitHandlers<T>(string message, IConsumeContext context, IEnumerable<HandlerReference> handlerReferences, Type baseType = null) where T : Message
        {
            MethodInfo executeHandler = GetType().GetMethod("ExecuteHandler", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo genericexecuteHandler = (null != baseType) ? executeHandler.MakeGenericMethod(baseType) : executeHandler.MakeGenericMethod(typeof(T));

            foreach (HandlerReference handlerReference in handlerReferences)
            {
                object messageObject = JsonConvert.DeserializeObject(message, typeof (T));
                genericexecuteHandler.Invoke(this, new[] {messageObject, handlerReference.HandlerType, handlerReference.RoutingKeys, context});
            }

            string messageType = string.Empty;
            if (null != context && null != context.Headers && context.Headers.ContainsKey("MessageType"))
            {
                messageType = Encoding.UTF8.GetString((byte[]) context.Headers["MessageType"]);
            }

            // If the message was published (rather than sent), no need to scan for handlers interested in the BaseType messages...
            // The Publisher (owner of the contract) will explicitely publish all of the message's base types (if any).
            if (messageType != "Publish")
            {
                // Get message BaseType and call ProcessMessage recursively to see if there are any handlers interested in the BaseType
                Type newBaseType = (null != baseType) ? baseType.GetTypeInfo().BaseType : typeof (T).GetTypeInfo().BaseType;
                if (newBaseType != null && newBaseType.Name != typeof (object).Name)
                {
                    MethodInfo processMessage = GetType().GetMethod("ProcessMessageBaseType", BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo genericProcessMessage = processMessage.MakeGenericMethod(typeof (T), newBaseType);
                    genericProcessMessage.Invoke(this, new object[] {message, context});
                }
            }
        }

        private void ExecuteHandler<T>(T message, Type handlerType, IList<string> routingKeys, IConsumeContext context) where T : Message
        {
            Logger.DebugFormat("Handler = {0}. Handler RoutingKeys = {1}, Message = {2}", handlerType.Name, JsonConvert.SerializeObject(routingKeys), typeof(T).Name);

            // Ignore irelevant handlers
            if (null != context && null != context.Headers && context.Headers.ContainsKey("RoutingKey"))
            {
                string msgRoutingKey = Encoding.UTF8.GetString((byte[])context.Headers["RoutingKey"]);

                Logger.DebugFormat("Msg RoutingKey = {0}.", msgRoutingKey);

                if (!routingKeys.Contains(msgRoutingKey) && !routingKeys.Contains("#"))
                {
                    Logger.Debug("Ignoring handler execution.");
                    return;
                }
            }
            else
            {
                Logger.Debug("Msg with no RoutingKeys.");

                if (null != routingKeys && routingKeys.Any())
                {
                    Logger.Debug("Ignoring handler execution.");
                    return;
                }
            }
                
            // Execute handler
            try
            {
                var handler = (IMessageHandler<T>) _container.GetInstance(handlerType);
                handler.Context = context;

                Logger.DebugFormat("Executing {0}.", handlerType.Name);

                handler.Execute(message);
            }
            catch (Exception ex)
            {
                Logger.Error(string.Format("Error executing handler. {0}", handlerType.FullName), ex);
                throw;
            }
        }
    }
}