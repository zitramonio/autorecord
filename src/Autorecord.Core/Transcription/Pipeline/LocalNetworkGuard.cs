using System.Reflection;
using System.Reflection.Emit;
using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Transcription.Pipeline;

public static class LocalNetworkGuard
{
    private static readonly string[] RuntimeNamespacePrefixes =
    [
        "Autorecord.Core.Transcription",
        "Autorecord.Core.Transcription.Diarization",
        "Autorecord.Core.Transcription.Engines",
        "Autorecord.Core.Transcription.Jobs",
        "Autorecord.Core.Transcription.Models",
        "Autorecord.Core.Transcription.Pipeline",
        "Autorecord.Core.Transcription.Results"
    ];

    private static readonly string[] ForbiddenSourceTokens =
    [
        "HttpClient",
        "HttpMessageHandler",
        "HttpRequestMessage",
        "HttpResponseMessage",
        "HttpContent",
        "System.Net.Http",
        "WebClient",
        "System.Net.WebClient",
        "WebRequest",
        "HttpWebRequest",
        "FtpWebRequest",
        "HttpListener",
        "SocketsHttpHandler",
        "Socket",
        "TcpClient",
        "UdpClient",
        "Dns.",
        "NetworkStream",
        "ClientWebSocket",
        "WebSocket",
        "IHttpClientFactory"
    ];

    private static readonly string[] ForbiddenNetworkTypeNames =
    [
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpMessageHandler",
        "System.Net.Http.HttpRequestMessage",
        "System.Net.Http.HttpResponseMessage",
        "System.Net.Http.HttpContent",
        "System.Net.Http.SocketsHttpHandler",
        "System.Net.WebClient",
        "System.Net.WebRequest",
        "System.Net.HttpWebRequest",
        "System.Net.FtpWebRequest",
        "System.Net.HttpListener",
        "System.Net.Dns",
        "System.Net.Sockets.Socket",
        "System.Net.Sockets.TcpClient",
        "System.Net.Sockets.UdpClient",
        "System.Net.Sockets.NetworkStream",
        "System.Net.WebSockets.ClientWebSocket",
        "System.Net.WebSockets.WebSocket",
        "Microsoft.Extensions.Http.IHttpClientFactory"
    ];

    private static readonly OpCode[] OneByteOpCodes = BuildOpCodeMap(singleByte: true);
    private static readonly OpCode[] TwoByteOpCodes = BuildOpCodeMap(singleByte: false);

    public static void AssertTranscriptionRuntimeIsOffline()
    {
        var transcriptionTypes = typeof(LocalNetworkGuard)
            .Assembly
            .GetTypes()
            .Where(IsRuntimeType)
            .ToArray();

        var violations = FindViolations(transcriptionTypes);
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                "Transcription runtime contains forbidden network dependencies: " + string.Join("; ", violations));
        }
    }

    public static IReadOnlyList<string> FindViolations(IEnumerable<Type> types)
    {
        return types
            .Where(type => !IsDownloadBoundaryType(type))
            .SelectMany(FindTypeViolations)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindSourceViolations(string transcriptionSourceRoot)
    {
        var root = Path.GetFullPath(transcriptionSourceRoot);
        var sourceExemptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(root, "Models", "ModelDownloadService.cs")),
            Path.GetFullPath(Path.Combine(root, "Pipeline", "LocalNetworkGuard.cs"))
        };

        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !sourceExemptPaths.Contains(Path.GetFullPath(path)))
            .SelectMany(FindFileViolations)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsRuntimeType(Type type)
    {
        if (type.Namespace is null)
        {
            return false;
        }

        return RuntimeNamespacePrefixes.Any(prefix =>
            string.Equals(type.Namespace, prefix, StringComparison.Ordinal) ||
            type.Namespace.StartsWith(prefix + ".", StringComparison.Ordinal));
    }

    private static bool IsDownloadBoundaryType(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current == typeof(ModelDownloadService))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> FindFileViolations(string path)
    {
        var text = File.ReadAllText(path);
        foreach (var token in ForbiddenSourceTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
            {
                yield return $"{path} contains forbidden network token '{token}'";
            }
        }
    }

    private static IEnumerable<string> FindTypeViolations(Type type)
    {
        if (type.TypeInitializer is not null)
        {
            foreach (var violation in FindMethodBodyViolations(type, type.TypeInitializer))
            {
                yield return violation;
            }
        }

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (IsForbiddenNetworkType(parameter.ParameterType))
                {
                    yield return $"{type.FullName} constructor parameter '{parameter.Name}' uses {parameter.ParameterType.Name}";
                }
            }

            foreach (var violation in FindMethodBodyViolations(type, constructor))
            {
                yield return violation;
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (IsForbiddenNetworkType(field.FieldType))
            {
                yield return $"{type.FullName} field '{field.Name}' uses {field.FieldType.Name}";
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (IsForbiddenNetworkType(property.PropertyType))
            {
                yield return $"{type.FullName} property '{property.Name}' uses {property.PropertyType.Name}";
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (IsForbiddenNetworkType(method.ReturnType))
            {
                yield return $"{type.FullName} method '{method.Name}' returns {method.ReturnType.Name}";
            }

            foreach (var parameter in method.GetParameters())
            {
                if (IsForbiddenNetworkType(parameter.ParameterType))
                {
                    yield return $"{type.FullName} method '{method.Name}' parameter '{parameter.Name}' uses {parameter.ParameterType.Name}";
                }
            }

            foreach (var violation in FindMethodBodyViolations(type, method))
            {
                yield return violation;
            }
        }
    }

    private static IEnumerable<string> FindMethodBodyViolations(Type ownerType, MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null || il.Length == 0)
        {
            yield break;
        }

        var position = 0;
        while (position < il.Length)
        {
            var opCode = ReadOpCode(il, ref position);
            if (!TryReadMetadataToken(il, opCode, ref position, out var token))
            {
                SkipOperand(il, opCode, ref position);
                continue;
            }

            var member = ResolveMember(method.Module, token);
            var forbiddenTypeName = member is null ? null : GetForbiddenNetworkTypeName(member);
            if (forbiddenTypeName is not null)
            {
                yield return $"{ownerType.FullName} method '{method.Name}' uses {forbiddenTypeName} in IL";
            }
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int position)
    {
        var value = il[position++];
        if (value != 0xFE)
        {
            return OneByteOpCodes[value];
        }

        return TwoByteOpCodes[il[position++]];
    }

    private static bool TryReadMetadataToken(byte[] il, OpCode opCode, ref int position, out int token)
    {
        switch (opCode.OperandType)
        {
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineTok:
            case OperandType.InlineType:
                token = BitConverter.ToInt32(il, position);
                position += 4;
                return true;
            default:
                token = 0;
                return false;
        }
    }

    private static void SkipOperand(byte[] il, OpCode opCode, ref int position)
    {
        position += opCode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineI => 4,
            OperandType.InlineR => 8,
            OperandType.InlineI8 => 8,
            OperandType.ShortInlineR => 4,
            OperandType.InlineString => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineSwitch => SkipSwitchOperand(il, position),
            _ => 0
        };
    }

    private static int SkipSwitchOperand(byte[] il, int position)
    {
        var count = BitConverter.ToInt32(il, position);
        return 4 + count * 4;
    }

    private static MemberInfo? ResolveMember(Module module, int metadataToken)
    {
        try
        {
            return module.ResolveMember(metadataToken);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? GetForbiddenNetworkTypeName(MemberInfo member)
    {
        return member switch
        {
            Type type => GetForbiddenNetworkTypeName(type),
            FieldInfo field => GetForbiddenNetworkTypeName(field.FieldType)
                ?? GetForbiddenNetworkTypeName(field.DeclaringType),
            MethodInfo method => GetForbiddenNetworkTypeName(method.ReturnType)
                ?? GetForbiddenNetworkTypeName(method.DeclaringType)
                ?? method.GetParameters().Select(parameter => GetForbiddenNetworkTypeName(parameter.ParameterType)).FirstOrDefault(name => name is not null),
            ConstructorInfo constructor => GetForbiddenNetworkTypeName(constructor.DeclaringType)
                ?? constructor.GetParameters().Select(parameter => GetForbiddenNetworkTypeName(parameter.ParameterType)).FirstOrDefault(name => name is not null),
            _ => GetForbiddenNetworkTypeName(member.DeclaringType)
        };
    }

    private static bool IsForbiddenNetworkType(Type type)
    {
        return GetForbiddenNetworkTypeName(type) is not null;
    }

    private static string? GetForbiddenNetworkTypeName(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        var unwrapped = Unwrap(type);
        if (unwrapped.IsGenericType)
        {
            var genericArgumentMatch = unwrapped
                .GetGenericArguments()
                .Select(GetForbiddenNetworkTypeName)
                .FirstOrDefault(name => name is not null);
            if (genericArgumentMatch is not null)
            {
                return genericArgumentMatch;
            }
        }

        return EnumerateSelfBaseTypesAndInterfaces(unwrapped)
            .Select(candidate => candidate.FullName)
            .FirstOrDefault(name => name is not null &&
                ForbiddenNetworkTypeNames.Contains(name, StringComparer.Ordinal));
    }

    private static IEnumerable<Type> EnumerateSelfBaseTypesAndInterfaces(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            yield return interfaceType;
        }
    }

    private static Type Unwrap(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType() ?? type;
        }

        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    private static OpCode[] BuildOpCodeMap(bool singleByte)
    {
        var opCodes = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = unchecked((ushort)opCode.Value);
            if (singleByte && value <= 0xFF)
            {
                opCodes[value] = opCode;
            }
            else if (!singleByte && (value & 0xFF00) == 0xFE00)
            {
                opCodes[value & 0xFF] = opCode;
            }
        }

        return opCodes;
    }
}
