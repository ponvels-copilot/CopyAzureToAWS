using AutoFixture;
using AutoFixture.AutoMoq;
using Moq;
using Amazon.Lambda.TestUtilities;

namespace AzureToAWS.Processor.Lambda.Tests;

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