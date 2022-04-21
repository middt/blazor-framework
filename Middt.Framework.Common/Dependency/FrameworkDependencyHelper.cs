using Microsoft.Extensions.DependencyInjection;
using System;

namespace Middt.Framework.Common.Dependency
{
    public class FrameworkDependencyHelper
    {
        private static readonly Lazy<FrameworkDependencyHelper> LazyInstance = new Lazy<FrameworkDependencyHelper>(() => new FrameworkDependencyHelper());
        public static FrameworkDependencyHelper Instance
        {
            get { return LazyInstance.Value; }
        }

        protected IServiceCollection serviceCollection;
        protected ServiceProvider provider;
        protected FrameworkDependencyHelper()
        {
            serviceCollection = new ServiceCollection();
            ReloadDependency();
        }

        public void LoadServiceCollection(IServiceCollection _serviceCollection)
        {
            serviceCollection = _serviceCollection;
            ReloadDependency();
        }
        public void ReloadDependency()
        {
            provider = serviceCollection.BuildServiceProvider();
        }
        public TClass Get<TClass>()
        {
            return provider.GetService<TClass>();
        }

        public void AddSingleton<TClass>()
        {
            serviceCollection.AddSingleton(typeof(TClass));
            ReloadDependency();
        }
        public void AddTransient<TClass>()
        {
            serviceCollection.AddTransient(typeof(TClass));
            ReloadDependency();
        }
        public void AddScoped<TClass>()
        {
            serviceCollection.AddScoped(typeof(TClass));
            ReloadDependency();
        }


    }
}
