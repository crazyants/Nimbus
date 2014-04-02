﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;
using Nimbus.Extensions;
using Nimbus.HandlerFactories;
using Nimbus.Handlers;
using Nimbus.MessageContracts;

namespace Nimbus.Infrastructure.RequestResponse
{
    internal class RequestMessageDispatcher : IMessageDispatcher
    {
        private readonly INimbusMessagingFactory _messagingFactory;
        private readonly IBrokeredMessageFactory _brokeredMessageFactory;
        private readonly Type _messageType;
        private readonly IRequestHandlerFactory _requestHandlerFactory;
        private readonly IClock _clock;

        public RequestMessageDispatcher(
            INimbusMessagingFactory messagingFactory,
            IBrokeredMessageFactory brokeredMessageFactory,
            Type messageType,
            IRequestHandlerFactory requestHandlerFactory,
            IClock clock)
        {
            _messagingFactory = messagingFactory;
            _brokeredMessageFactory = brokeredMessageFactory;
            _messageType = messageType;
            _requestHandlerFactory = requestHandlerFactory;
            _clock = clock;
        }

        public async Task Dispatch(BrokeredMessage message)
        {
            var request = message.GetBody(_messageType);
            var dispatchMethod = GetGenericDispatchMethodFor(request);
            await (Task) dispatchMethod.Invoke(this, new[] {request, message});
        }

        private async Task Dispatch<TBusRequest, TBusResponse>(TBusRequest busRequest, BrokeredMessage message)
            where TBusRequest : IBusRequest<TBusRequest, TBusResponse>
            where TBusResponse : IBusResponse
        {
            var replyQueueName = message.ReplyTo;
            var replyQueueClient = _messagingFactory.GetQueueSender(replyQueueName);

            BrokeredMessage responseMessage;
            try
            {
                using (var handler = _requestHandlerFactory.GetHandler<TBusRequest, TBusResponse>())
                {
                    var handlerTask = handler.Component.Handle(busRequest);
                    var wrapperTask = new LongLivedTaskWrapper<TBusResponse>(handlerTask, handler.Component as ILongRunningHandler, message, _clock);
                    var response = await wrapperTask.AwaitCompletion();
                    responseMessage = _brokeredMessageFactory.CreateSuccessfulResponse(response, message);
                }
            }
            catch (Exception exc)
            {
                responseMessage = _brokeredMessageFactory.CreateFailedResponse(message, exc);
            }

            await replyQueueClient.Send(responseMessage);
        }

        internal static MethodInfo GetGenericDispatchMethodFor(object request)
        {
            var closedGenericTypeOfIBusRequest = request.GetType()
                                                        .GetInterfaces()
                                                        .Where(t => t.IsClosedTypeOf(typeof (IBusRequest<,>)))
                                                        .Single();

            var genericArguments = closedGenericTypeOfIBusRequest.GetGenericArguments();
            var requestType = genericArguments[0];
            var responseType = genericArguments[1];

            var openGenericMethod = typeof (RequestMessageDispatcher).GetMethod("Dispatch", BindingFlags.NonPublic | BindingFlags.Instance);
            var closedGenericMethod = openGenericMethod.MakeGenericMethod(new[] {requestType, responseType});
            return closedGenericMethod;
        }
    }
}