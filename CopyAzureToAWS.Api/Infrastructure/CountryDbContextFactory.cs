using AzureToAWS.Api.Configuration;
using AzureToAWS.Data;
using Microsoft.EntityFrameworkCore;

namespace AzureToAWS.Api.Infrastructure
{
    public interface ICountryDbContextFactory
    {
        ApplicationDbContext CreateWriter(string countryCode);
        ApplicationDbContext CreateReader(string countryCode);
    }

    public class CountryDbContextFactory : ICountryDbContextFactory
    {
        private readonly IConnectionStringResolver _resolver;

        public CountryDbContextFactory(IConnectionStringResolver resolver) => _resolver = resolver;

        public ApplicationDbContext CreateWriter(string countryCode)
            => Build(_resolver.GetWriter(countryCode));

        public ApplicationDbContext CreateReader(string countryCode)
            => Build(_resolver.GetReader(countryCode));

        private static ApplicationDbContext Build(string cs)
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(cs)
                .EnableSensitiveDataLogging(false)
                .Options;
            return new ApplicationDbContext(opts);
        }
    }
}