using Controledu.Teacher.Server.Options;
using Controledu.Teacher.Server.Services;
using Microsoft.Extensions.Options;

namespace Controledu.Tests;

public sealed class PairingCodeServiceTests
{
    [Fact]
    public void GeneratedPin_IsConsumedOnlyOnce()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new PairingCodeService(Options.Create(new TeacherServerOptions { PairingPinLifetimeSeconds = 60 }), clock);

        var pin = service.Generate();

        Assert.True(service.TryConsume(pin.PinCode));
        Assert.False(service.TryConsume(pin.PinCode));
    }

    [Fact]
    public void Pin_ExpiresAfterLifetime()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new PairingCodeService(Options.Create(new TeacherServerOptions { PairingPinLifetimeSeconds = 1 }), clock);

        var pin = service.Generate();
        Assert.True(service.IsValid(pin.PinCode));

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.False(service.IsValid(pin.PinCode));
        Assert.False(service.TryConsume(pin.PinCode));
    }

    private sealed class FakeClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }
}
