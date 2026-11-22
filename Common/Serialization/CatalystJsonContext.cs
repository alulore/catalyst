using System.Text.Json.Serialization;
using Catalyst.Commands.Api;
using Catalyst.Commands.Gen;
using Catalyst.Commands.Mcp;
using Catalyst.Common.Configuration;

namespace Catalyst.Common.Serialization;

/// <summary>
/// The source-generated serializer metadata of every JSON document the CLI reads or writes, so nothing is
/// serialized by reflection and the code stays safe to trim and to compile ahead of time.
/// </summary>
[JsonSerializable(typeof(RunReport))]
[JsonSerializable(typeof(ApiReport))]
[JsonSerializable(typeof(ListClientsReport))]
[JsonSerializable(typeof(ListClientsFailure))]
[JsonSerializable(typeof(CatalystConfig))]
[JsonSerializable(typeof(string))]
internal sealed partial class CatalystJsonContext : JsonSerializerContext { }
