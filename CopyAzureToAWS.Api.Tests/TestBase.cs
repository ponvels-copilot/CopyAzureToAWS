using AutoFixture;
using AutoFixture.AutoMoq;
using Amazon.Lambda.TestUtilities;
using Moq;

namespace AzureToAWS.Api.Tests;

public abstract class TestBase
{
    protected readonly IFixture Fixture;
    protected readonly TestLambdaContext Context;

    protected TestBase()
    {
        Fixture = new Fixture()
            .Customize(new AutoMoqCustomization());
        
        Context = new TestLambdaContext();
    }

    protected Mock<T> Mock<T>() where T : class
    {
        return Fixture.Freeze<Mock<T>>();
    }
}