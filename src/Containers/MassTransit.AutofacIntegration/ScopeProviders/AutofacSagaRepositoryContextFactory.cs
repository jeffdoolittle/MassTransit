namespace MassTransit.AutofacIntegration.ScopeProviders
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Autofac;
    using Automatonymous;
    using Context;
    using GreenPipes;
    using Saga;


    public class AutofacSagaRepositoryContextFactory<TSaga> :
        ISagaRepositoryContextFactory<TSaga>
        where TSaga : class, ISaga
    {
        const string DefaultScopeName = "message";
        readonly string _name;

        readonly ILifetimeScopeProvider _scopeProvider;

        public AutofacSagaRepositoryContextFactory(ILifetimeScope lifetimeScope)
        {
            _scopeProvider = new SingleLifetimeScopeProvider(lifetimeScope);
            _name = DefaultScopeName;
        }

        public AutofacSagaRepositoryContextFactory(ILifetimeScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
            _name = DefaultScopeName;
        }

        public AutofacSagaRepositoryContextFactory(ILifetimeScopeProvider scopeProvider, string name)
        {
            _scopeProvider = scopeProvider;
            _name = name ?? DefaultScopeName;
        }

        public void Probe(ProbeContext context)
        {
            context.Add("provider", "dependencyInjection");
            context.Add("scopeTag", _name);
        }

        public Task Send<T>(ConsumeContext<T> context, IPipe<SagaRepositoryContext<TSaga, T>> next)
            where T : class
        {
            return Send(context, (consumeContext, factory) => factory.Send(consumeContext, next));
        }

        public Task SendQuery<T>(ConsumeContext<T> context, ISagaQuery<TSaga> query, IPipe<SagaRepositoryQueryContext<TSaga, T>> next)
            where T : class
        {
            return Send(context, (consumeContext, factory) => factory.SendQuery(consumeContext, query, next));
        }

        public async Task<T> Execute<T>(Func<SagaRepositoryContext<TSaga>, Task<T>> asyncMethod, CancellationToken cancellationToken)
            where T : class
        {
            using var scope = _scopeProvider.LifetimeScope.BeginLifetimeScope(_name);

            var factory = scope.Resolve<ISagaRepositoryContextFactory<TSaga>>();

            return await factory.Execute(asyncMethod, cancellationToken).ConfigureAwait(false);
        }

        Task Send<T>(ConsumeContext<T> context, Func<ConsumeContext<T>, ISagaRepositoryContextFactory<TSaga>, Task> send)
            where T : class
        {
            if (context.TryGetPayload<ILifetimeScope>(out var existingScope))
            {
                context.GetOrAddPayload(() => existingScope.ResolveOptional<IStateMachineActivityFactory>() ?? AutofacStateMachineActivityFactory.Instance);

                var factory = existingScope.Resolve<ISagaRepositoryContextFactory<TSaga>>();

                return send(context, factory);
            }

            var parentLifetimeScope = _scopeProvider.GetLifetimeScope(context);

            async Task CreateScope()
            {
                using var scope = parentLifetimeScope.BeginLifetimeScope(_name, builder => builder.ConfigureScope(context));

                var activityFactory = scope.ResolveOptional<IStateMachineActivityFactory>() ?? AutofacStateMachineActivityFactory.Instance;

                var consumeContextScope = new ConsumeContextScope<T>(context, scope, activityFactory);

                var factory = scope.Resolve<ISagaRepositoryContextFactory<TSaga>>();

                await send(consumeContextScope, factory).ConfigureAwait(false);
            }

            return CreateScope();
        }
    }
}
