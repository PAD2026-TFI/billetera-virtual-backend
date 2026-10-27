using Xunit;

namespace Wallet.Tests;

// Las carreras se coordinan dentro de cada prueba. No compito con fixtures SQL de otras clases.
[CollectionDefinition("SqlIntegration", DisableParallelization = true)]
public sealed class SqlIntegrationCollection { }
