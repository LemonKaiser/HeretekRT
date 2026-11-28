using System.Linq;
using Content.Shared._WH40K.Wave;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K;

public sealed class WaveShaderTest
{
    private static readonly ProtoId<ShaderPrototype> SelectionOutlineShader = "SelectionOutline";

    [Test]
    public async Task WaveShaderPreservesInteractionOutline()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entities = client.ResolveDependency<IEntityManager>();
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var sprites = client.System<SpriteSystem>();

        await client.WaitAssertion(() =>
        {
            const string outlineId = "interaction-outline";
            var uid = entities.Spawn();
            var sprite = entities.AddComponent<SpriteComponent>(uid);
            var outline = prototypes.Index(SelectionOutlineShader).Instance();
            sprites.SetPostShader(sprite, new SpriteComponent.PostShaderArgs(outlineId, outline));

            entities.AddComponent<WH40KWaveShaderComponent>(uid);
            Assert.That(sprites.GetPostShaders(sprite).Select(shader => shader.Id),
                Is.EqualTo(new[] { "wh40k-wave", outlineId }));

            entities.RemoveComponent<WH40KWaveShaderComponent>(uid);
            Assert.That(sprites.GetPostShaders(sprite).Select(shader => shader.Id), Is.EqualTo(new[] { outlineId }));
            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }
}
