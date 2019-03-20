using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Phema.Serialization;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client.Impl;

namespace Phema.RabbitMQ
{
	public interface IRabbitMQProducersBuilder
	{
		/// <summary>
		///   Add new producer
		/// </summary>
		IRabbitMQProducerBuilder AddProducer<TPayload>(string exchangeName, string queueName = null);
	}

	internal sealed class RabbitMQProducersBuilder : IRabbitMQProducersBuilder
	{
		private readonly IConnection connection;
		private readonly IServiceCollection services;

		public RabbitMQProducersBuilder(IServiceCollection services, IConnection connection)
		{
			this.services = services;
			this.connection = connection;
		}

		public IRabbitMQProducerBuilder AddProducer<TPayload>(string exchangeName, string queueName = null)
		{
			if (exchangeName is null)
				throw new ArgumentNullException(nameof(exchangeName));

			var metadata = new RabbitMQProducerMetadata(exchangeName, queueName);

			services.TryAddSingleton<IRabbitMQProducer<TPayload>>(provider =>
			{
				var channel = (IFullModel) connection.CreateModel();

				var exchange = provider.GetRequiredService<IOptions<RabbitMQExchangesOptions>>()
					.Value
					.Exchanges
					.FirstOrDefault(ex => ex.Name == metadata.ExchangeName);

				// It can be default exchange or already declared
				// So no reason to write configuration for it
				if (exchange != null)
				{
					channel._Private_ExchangeDeclare(
						exchange: exchange.Name,
						type: exchange.Type,
						passive: false,
						durable: exchange.Durable,
						autoDelete: exchange.AutoDelete,
						@internal: exchange.Internal,
						nowait: exchange.NoWait,
						arguments: exchange.Arguments);

					foreach (var binding in exchange.ExchangeBindings)
					{
						channel._Private_ExchangeBind(
							destination: binding.ExchangeName,
							source: exchange.Name,
							routingKey: binding.RoutingKey ?? binding.ExchangeName,
							nowait: binding.NoWait,
							arguments: binding.Arguments);
					}
				}

				if (metadata.QueueName != null)
				{
					// Ensure queue declared in broker
					if (metadata.Mandatory)
					{
						try
						{
							channel.QueueDeclarePassive(metadata.QueueName);
						}
						catch (OperationInterruptedException exception)
						{
							throw new RabbitMQProducerException(
								$"Producer from '{metadata.ExchangeName}' exchange has specified '{nameof(metadata.QueueName)}' and " +
								$"'{nameof(metadata.Mandatory)}' flag, but queue '{metadata.QueueName}' does not declared in broker",
								exception);
						}
						
					}

					// Should bind queue with exchange when not declared,
					// because of default or already declared
					try
					{
						channel._Private_QueueBind(
							queue: metadata.QueueName,
							exchange: metadata.ExchangeName,
							routingKey: metadata.RoutingKey ?? metadata.QueueName,
							nowait: exchange?.NoWait ?? false,
							arguments: metadata.Arguments);
					}
					catch (OperationInterruptedException exception)
					{
						throw new RabbitMQProducerException(
							$"Producer from '{metadata.ExchangeName}' exchange has specified '{nameof(metadata.QueueName)}', " +
							$"but queue '{metadata.QueueName}' does not declared in broker",
							exception);
					}
				}

				var serializer = provider.GetRequiredService<ISerializer>();

				var properties = channel.CreateBasicProperties();
				foreach (var property in metadata.Properties)
				{
					property(properties);
				}

				if (metadata.WaitForConfirms)
				{
					channel.ConfirmSelect();
				}

				return new RabbitMQProducer<TPayload>(channel, serializer, metadata, properties);
			});

			return new RabbitMQProducerBuilder(metadata);
		}
	}
}