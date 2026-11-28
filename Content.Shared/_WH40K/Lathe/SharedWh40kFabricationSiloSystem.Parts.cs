using System.Linq;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Lathe;

public abstract partial class SharedWh40kFabricationSiloSystem
{
    public bool IsRecipePart(EntityUid item, EntProtoId part)
    {
        if (MetaData(item).EntityPrototype is not { } proto)
            return false;

        if (proto.ID == part.Id)
            return true;

        if (!_prototype.TryIndex(part, out var partProto))
            return false;

        if (_stackQuery.TryComp(item, out var stack) &&
            partProto.TryGetComponent<StackComponent>(out var partStack, _factory))
            return stack.StackTypeId == partStack.StackTypeId;

        if (proto.Name == partProto.Name && proto.Parents is { } parents && partProto.Parents is { } partParents &&
            parents.SequenceEqual(partParents))
            return true;

        if (proto.Parents != null)
        {
            var visited = new HashSet<string> { proto.ID };
            var pending = new Stack<string>(proto.Parents);
            while (pending.Count > 0)
            {
                var parentId = pending.Pop();
                if (!visited.Add(parentId))
                    continue;

                if (parentId == part.Id)
                    return true;

                if (_prototype.TryIndex<EntityPrototype>(parentId, out var parentProto) && parentProto.Parents != null)
                {
                    foreach (var gp in parentProto.Parents)
                        pending.Push(gp);
                }
            }
        }

        return false;
    }
}
