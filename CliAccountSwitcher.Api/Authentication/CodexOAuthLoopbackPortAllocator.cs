using CliAccountSwitcher.Api.Models;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CliAccountSwitcher.Api.Authentication;

internal static class CodexOAuthLoopbackPortAllocator
{
    public static IReadOnlyList<int> FindAvailablePorts(int minimumPort, int maximumPort, int maximumCandidateCount)
    {
        ValidatePortRange(minimumPort, maximumPort);
        if (maximumCandidateCount <= 0) throw new CodexApiException("The maximum OAuth candidate port count must be greater than zero.");

        var availablePorts = new List<int>();
        var visitedPorts = new HashSet<int>();
        var totalPortCount = maximumPort - minimumPort + 1;
        var targetPortCount = Math.Min(totalPortCount, maximumCandidateCount);

        while (visitedPorts.Count < totalPortCount && availablePorts.Count < targetPortCount)
        {
            var candidatePort = RandomNumberGenerator.GetInt32(minimumPort, maximumPort + 1);
            if (!visitedPorts.Add(candidatePort)) continue;
            if (!CanBindToPort(candidatePort)) continue;
            availablePorts.Add(candidatePort);
        }

        if (availablePorts.Count == 0) throw new CodexApiException($"An available OAuth callback port could not be found in the loopback range {minimumPort}-{maximumPort}.");
        return availablePorts;
    }

    public static int AllocateAvailablePort(int minimumPort, int maximumPort)
    {
        var availablePorts = FindAvailablePorts(minimumPort, maximumPort, 1);
        return availablePorts[0];
    }

    public static int ValidateFixedPort(int port)
    {
        if (port is < 1025 or > 65535) throw new CodexApiException("The OAuth redirect port must be between 1025 and 65535.");
        return port;
    }

    private static void ValidatePortRange(int minimumPort, int maximumPort)
    {
        if (minimumPort is < 1025 or > 65535) throw new CodexApiException("The minimum OAuth redirect port must be between 1025 and 65535.");
        if (maximumPort is < 1025 or > 65535) throw new CodexApiException("The maximum OAuth redirect port must be between 1025 and 65535.");
        if (minimumPort > maximumPort) throw new CodexApiException("The OAuth redirect port range is invalid.");
    }

    private static bool CanBindToPort(int port)
    {
        try
        {
            using var tcpListener = new TcpListener(IPAddress.Loopback, port);
            tcpListener.Start();
            tcpListener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
