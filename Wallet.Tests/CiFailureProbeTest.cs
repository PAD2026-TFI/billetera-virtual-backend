using Xunit;

namespace Wallet.Tests;

public sealed class CiFailureProbeTests
{
 [Fact]
public void Controller_failure_checks_ci_detection(){
    Assert.True(false, "Falla de prueba para validar ENV-016");
}
}