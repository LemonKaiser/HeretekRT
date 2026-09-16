using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server._WH40K.OperationalDomain;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using OperationalDomainData = Content.Shared._WH40K.OperationalDomain.OperationalDomain;

namespace Content.Server.Research.Systems
{
    [UsedImplicitly]
    public sealed partial class ResearchSystem : SharedResearchSystem
    {
        [Dependency] private IAdminLogManager _adminLog = default!;
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private AccessReaderSystem _accessReader = default!;
        [Dependency] private UserInterfaceSystem _uiSystem = default!;
        [Dependency] private SharedPopupSystem _popup = default!;
        [Dependency] private RadioSystem _radio = default!;
        [Dependency] private OperationalDomainSystem _operationalDomains = default!;
        [Dependency] private EntityLookupSystem _lookup = default!;

        public override void Initialize()
        {
            base.Initialize();
            InitializeClient();
            InitializeConsole();
            InitializeSource();
            InitializeServer();

            SubscribeLocalEvent<TechnologyDatabaseComponent, ResearchRegistrationChangedEvent>(OnDatabaseRegistrationChanged);
        }

        /// <summary>
        /// Gets a server based on it's unique numeric id.
        /// </summary>
        /// <param name="client">The client requesting a server.</param>
        /// <param name="id"></param>
        /// <param name="serverUid"></param>
        /// <param name="serverComponent"></param>
        /// <returns></returns>
        public bool TryGetServerById(EntityUid client, int id, [NotNullWhen(true)] out EntityUid? serverUid, [NotNullWhen(true)] out ResearchServerComponent? serverComponent)
        {
            serverUid = null;
            serverComponent = null;

            if (!_operationalDomains.TryResolveOperationalDomain(client, out var clientDomain))
                return false;

            var query = EntityQueryEnumerator<ResearchServerComponent>();
            while (query.MoveNext(out var uid, out var server))
            {
                if (server.Id != id || !IsInSameOperationalDomain(clientDomain, uid))
                    continue;
                serverUid = uid;
                serverComponent = server;
                return true;
            }
            return false;
        }

        private bool IsInSameOperationalDomain(EntityUid first, EntityUid second)
        {
            return _operationalDomains.TryResolveOperationalDomain(first, out var firstDomain) &&
                   IsInSameOperationalDomain(firstDomain, second);
        }

        private bool IsInSameOperationalDomain(OperationalDomainData firstDomain, EntityUid second)
        {
            return _operationalDomains.TryResolveOperationalDomain(second, out var secondDomain) &&
                   firstDomain.Owner == secondDomain.Owner;
        }

        /// <summary>
        /// Gets the names of all the servers.
        /// </summary>
        /// <returns></returns>
        public string[] GetServerNames()
        {
            var allServers = EntityQuery<ResearchServerComponent>(true).ToArray();
            var list = new string[allServers.Length];

            for (var i = 0; i < allServers.Length; i++)
            {
                list[i] = allServers[i].ServerName;
            }

            return list;
        }

        /// <summary>
        /// Gets the ids of all the servers
        /// </summary>
        /// <returns></returns>
        public int[] GetServerIds()
        {
            var allServers = EntityQuery<ResearchServerComponent>(true).ToArray();
            var list = new int[allServers.Length];

            for (var i = 0; i < allServers.Length; i++)
            {
                list[i] = allServers[i].Id;
            }

            return list;
        }

        /// <summary>
        /// Frontier copies of the original get servers. Research servers are isolated by operational domain.
        /// </summary>
        /// <param name="domainEntity">An entity in the domain whose servers are requested.</param>
        /// <returns></returns>
        public string[] GetNFServerNames(EntityUid domainEntity)
        {
            var allServers = EntityQueryEnumerator<ResearchServerComponent>();
            var list = new List<string>();

            if (_operationalDomains.TryResolveOperationalDomain(domainEntity, out var requestedDomain))
            {
                while (allServers.MoveNext(out var uid, out var comp))
                {
                    if (_operationalDomains.TryResolveOperationalDomain(uid, out var serverDomain) &&
                        serverDomain.Owner == requestedDomain.Owner)
                        list.Add(comp.ServerName);
                }
            }

            var serverList = list.ToArray();
            return serverList;
        }

        public int[] GetNFServerIds(EntityUid domainEntity)
        {
            var allServers = EntityQueryEnumerator<ResearchServerComponent>();
            var list = new List<int>();

            if (_operationalDomains.TryResolveOperationalDomain(domainEntity, out var requestedDomain))
            {
                while (allServers.MoveNext(out var uid, out var comp))
                {
                    if (_operationalDomains.TryResolveOperationalDomain(uid, out var serverDomain) &&
                        serverDomain.Owner == requestedDomain.Owner)
                        list.Add(comp.Id);
                }
            }

            var serverList = list.ToArray();
            return serverList;
        }

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<ResearchServerComponent>();
            while (query.MoveNext(out var uid, out var server))
            {
                if (server.NextUpdateTime > _timing.CurTime)
                    continue;
                server.NextUpdateTime = _timing.CurTime + server.ResearchConsoleUpdateTime;

                UpdateServer(uid, (int) server.ResearchConsoleUpdateTime.TotalSeconds, server);
            }
        }
    }
}
