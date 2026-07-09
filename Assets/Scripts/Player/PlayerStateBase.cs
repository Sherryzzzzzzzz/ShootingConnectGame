using InputFrame = ShootingGame.Shared.Simulation.InputFrame;
public abstract class PlayerStateBase : StateBase
{
    protected PlayerModel playerModel;

    // 玩家控制器（惰性解析，解决 PlayerModel.Start() 在 NetPlayerController 添加前执行的问题）
    private NetPlayerController _cachedController;
    protected NetPlayerController playerController
    {
        get
        {
            if (_cachedController == null && playerModel != null)
            {
                _cachedController = playerModel.GetComponent<NetPlayerController>();
            }
            return _cachedController;
        }
    }

    public override void Init(IStateOwner owner)
    {
        playerModel = owner as PlayerModel;
        _cachedController = playerModel != null ? playerModel.GetComponent<NetPlayerController>() : null;
        Init(playerModel);
    }

    public virtual void Init(PlayerModel model)
    {
        playerModel = model;
        // playerController 通过上面的属性惰性获取
    }

    public override void Enter() { }
    public override void Exit() { }
    public override void Update() { }
    public override void Destroy() { }

    public override void Tick(InputFrame input, float dt) { }
}