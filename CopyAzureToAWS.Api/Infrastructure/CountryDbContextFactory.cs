using CopyAzureToAWS.Api.Configuration;
using CopyAzureToAWS.Data;
using Microsoft.EntityFrameworkCore;

namespace CopyAzureToAWS.Api.Infrastructure
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