using Xunit;
namespace LibraryReview.Tests;
public sealed class ConfirmationTests
{
    private readonly Guid _id = Guid.NewGuid();
    private PendingReplacement Pending(DateTimeOffset? expiry=null) => new(
        new("ticket",_id,"Example","/movies/example.mkv",4,"hash","release",1,2,expiry ?? DateTimeOffset.UtcNow.AddMinutes(10)),
        new("/movies/example.mkv",4,123),new(1,2,3,"/movies/example.mkv",4,"release"),"config-v1");
    [Fact] public void DuplicateSubmissionCannotRunTwice()
    {
        var store=new ConfirmationStore();store.Add(Pending());
        store.Consume(_id,new("ticket","Example"),"config-v1",true);
        Assert.Throws<InvalidOperationException>(()=>store.Consume(_id,new("ticket","Example"),"config-v1",true));
    }
    [Fact] public void ExpiredPreviewRejected()
    {
        var store=new ConfirmationStore();store.Add(Pending(DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Throws<InvalidOperationException>(()=>store.Consume(_id,new("ticket","Example"),"config-v1",true));
    }
    [Fact] public void ConfigurationChangeInvalidatesPreview()
    {
        var store=new ConfirmationStore();store.Add(Pending());
        Assert.Throws<InvalidOperationException>(()=>store.Consume(_id,new("ticket","Example"),"config-v2",true));
    }
    [Fact] public void WrongTitleRejected()
    {
        var store=new ConfirmationStore();store.Add(Pending());
        Assert.Throws<InvalidOperationException>(()=>store.Consume(_id,new("ticket","Other"),"config-v1",true));
    }
    [Fact] public void DifferentItemRejected()
    {
        var store=new ConfirmationStore();store.Add(Pending());
        Assert.Throws<InvalidOperationException>(()=>store.Consume(Guid.NewGuid(),new("ticket","Example"),"config-v1",true));
    }
    [Fact] public void DisabledReplacementRejected()
    {
        var store=new ConfirmationStore();store.Add(Pending());
        Assert.Throws<InvalidOperationException>(()=>store.Consume(_id,new("ticket","Example"),"config-v1",false));
    }
}
