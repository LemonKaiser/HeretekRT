namespace Content.Client.Launcher;

public sealed class ConnectingTargetManager
{
    public string? Address { get; private set; }
    public string? Host { get; private set; }
    public ushort? Port { get; private set; }
    public bool JoinLobbyAfterConnect { get; private set; }

    public bool HasManualTarget => !string.IsNullOrWhiteSpace(Address)
                                   && !string.IsNullOrWhiteSpace(Host)
                                   && Port != null;

    public void SetManualTarget(string address, string host, ushort port, bool joinLobbyAfterConnect = false)
    {
        Address = address.Trim();
        Host = host;
        Port = port;
        JoinLobbyAfterConnect = joinLobbyAfterConnect;
    }

    public void ClearJoinLobbyAfterConnect()
    {
        JoinLobbyAfterConnect = false;
    }

    public void Clear()
    {
        Address = null;
        Host = null;
        Port = null;
        JoinLobbyAfterConnect = false;
    }
}
