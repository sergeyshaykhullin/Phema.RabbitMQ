using System;
using System.Collections.Generic;

using RabbitMQ.Client;

namespace Phema.RabbitMQ
{
	internal sealed class RabbitMQProducer
	{
		public RabbitMQProducer(string exchangeName, string queueName)
		{
			ExchangeName = exchangeName;
			QueueName = queueName;
			Properties = new List<Action<IBasicProperties>>();
			Arguments = new Dictionary<string, object>();
		}

		public string ExchangeName { get; }
		public string QueueName { get; }
		public string RoutingKey { get; set; }
		public bool Mandatory { get; set; }
		public IList<Action<IBasicProperties>> Properties { get; }
		public IDictionary<string, object> Arguments { get; }
	}
}