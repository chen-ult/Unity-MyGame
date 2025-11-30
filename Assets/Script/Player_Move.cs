using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player_Move : MonoBehaviour
{
    [Header("基本移动参数")]
    public float moveSpeed = 5f;          // 地面最大移动速度
    public float groundAcceleration = 15f;// 地面加速强度（越大加速越快）
    public float groundDeceleration = 20f;// 地面减速强度（越大减速越快）
    public float airAcceleration = 8f;    // 空中加速强度（比地面小）
    public float airDeceleration = 5f;    // 空中减速强度（比地面小）

    [Header("跳跃参数")]
    public float jumpForce = 7f;          // 跳跃力度（高度）
    public float airJumpForce = 5f;       //二次跳跃力度
    public int maxJumpCount = 2;          // 最大跳跃次数（二段跳）
    public float coyoteTime = 0.1f;       // 落地后短时间仍可跳
    public float jumpBufferTime = 0.1f;   // 提前按跳，落地即触发

    [Header("接地检测参数")]
    public Vector2 groundCheckOffset = new Vector2(0f, -0.8f); // 检测点偏移（对准精灵底部）
    public float groundCheckRadius = 0.3f;                    // 检测半径
    public LayerMask groundLayer;                             // 地面图层




    // 私有变量
    private Rigidbody2D rb;
    private Collider2D coll;
    private float horizontalInput;
    private bool isGrounded;
    private int currentJumpCount;
    private float jumpBufferTimer;
    private float coyoteTimer;

    [Header("动画参数")]
    public Animator animator; 

    
    private readonly int isRunningHash = Animator.StringToHash("IsRunning");
    private readonly int isGroundedHash = Animator.StringToHash("IsGrounded");

    private void Awake()
    {
        // 获取组件（根对象必须挂载Rigidbody2D和Collider2D）
        rb = GetComponent<Rigidbody2D>();
        coll = GetComponent<Collider2D>();

        animator = GetComponentInChildren<Animator>();

        // 物理设置（关键：确保稳定无抽搐）
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 3.5f;              // 重力强度（适中，避免飘）
        rb.freezeRotation = true;            // 禁止旋转
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // 避免穿模
        rb.interpolation = RigidbodyInterpolation2D.Interpolate; // 开启插值，消除帧跳动
    }

    private void Update()
    {
        // 1. 获取输入
        horizontalInput = Input.GetAxisRaw("Horizontal");
        bool jumpInput = Input.GetButtonDown("Jump");

        // 2. 跳跃缓冲：按下跳键时重置计时器
        if (jumpInput)
        {
            jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            jumpBufferTimer -= Time.deltaTime;
        }

        // 3. Coyote时间：接地时重置，空中递减
        if (isGrounded)
        {
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
        }

        bool canGroundJump = jumpBufferTimer > 0f && coyoteTimer > 0f && currentJumpCount < maxJumpCount;

        bool canAirJump = jumpInput && !isGrounded && currentJumpCount < maxJumpCount && coyoteTimer < 0f;

        if(canGroundJump || canAirJump)
        {
            // 4. 执行跳跃
            Jump(currentJumpCount == 0);
            jumpBufferTimer = 0f; // 重置跳跃缓冲，防止多次触发
            coyoteTimer = 0f;    // 重置Coyote时间，防止多次触发
        }

        // 5. 精灵翻转（视觉跟随输入方向）
        FlipSprite();

        UpdateAnimatorStates();
    }

    private void UpdateAnimatorStates()
    {
        bool isRunning = false;

        // 只有接地时，才根据水平输入判断是否跑步
        if (isGrounded)
        {
            // 有明显水平输入（避免误触），则视为跑步
            isRunning = Mathf.Abs(horizontalInput) > 0.15f;
        }
        else
        {
            // 空中时，强制不跑步（播放idle）
            isRunning = false;
        }

        // 给Animator设置参数（控制动画切换）
        animator.SetBool(isRunningHash, isRunning);
        animator.SetBool(isGroundedHash, isGrounded);
    }

    private void FixedUpdate()
    {
        // 6. 接地检测（物理帧执行，更稳定）
        CheckGrounded();

        // 7. 计算目标水平速度（地面/空中差异化）
        float targetXVelocity = CalculateTargetXVelocity();

        // 8. 平滑过渡到目标速度（核心：用Lerp替代二阶动力学）
        float currentXVelocity = rb.velocity.x;
        float smoothedXVelocity = Mathf.Lerp(currentXVelocity, targetXVelocity, GetAccelerationRate() * Time.fixedDeltaTime);

        // 9. 应用速度（垂直速度保持物理引擎的重力效果）
        rb.velocity = new Vector2(smoothedXVelocity, rb.velocity.y);
    }

    // 接地检测：用圆形重叠检测，精准判断是否站在地面
    private void CheckGrounded()
    {
        Vector2 checkPos = (Vector2)transform.position + groundCheckOffset;
        bool isCurrentlyGrounded = Physics2D.OverlapCircle(
            checkPos,
            groundCheckRadius,
            groundLayer,
            -0.1f, 0.1f
        );

        // 接地时重置跳跃次数（支持再次跳跃/二段跳）
        if (isCurrentlyGrounded)
        {
            isGrounded = true;
            currentJumpCount = 0;
        }
        else
        {
            isGrounded = false;
        }

        // 给Animator设置接地状态
        animator.SetBool(isGroundedHash, isGrounded);
    }

    // 计算目标水平速度（地面/空中逻辑分离）
    private float CalculateTargetXVelocity()
    {
        float targetX = 0f;

        if (isGrounded)
        {
            // 地面：有输入时，目标速度=最大速度；无输入时，目标速度=0（快速减速）
            targetX = horizontalInput * moveSpeed;
        }
        else
        {
            // 空中：有输入时，目标速度=最大速度×空中控制系数；无输入时，目标速度=当前速度（缓慢减速）
            targetX = horizontalInput * moveSpeed * 0.6f; // 0.6f是空中控制系数（可调整）
        }

        return targetX;
    }

    // 获取当前加速强度（地面/空中不同）
    private float GetAccelerationRate()
    {
        if (isGrounded)
        {
            // 地面：有输入时加速，无输入时减速
            return Mathf.Abs(horizontalInput) > 0.15f ? groundAcceleration : groundDeceleration;
        }
        else
        {
            // 空中：有输入时加速，无输入时减速
            return Mathf.Abs(horizontalInput) > 0.15f ? airAcceleration : airDeceleration;
        }
    }

    // 跳跃核心逻辑：重置Y轴速度+施加向上冲量
    private void Jump(bool isGroundJump)
    {
        rb.velocity = new Vector2(rb.velocity.x, 0f); // 重置Y轴速度，避免下落时跳不高

        float RealjumpForce = isGroundJump ? jumpForce : airJumpForce;
        rb.AddForce(Vector2.up * RealjumpForce, ForceMode2D.Impulse); // 瞬间向上冲量（跳跃）
        currentJumpCount++; // 递增跳跃次数，限制二段跳
    }

    // 精灵翻转：通过修改水平缩放实现
    private void FlipSprite()
    {
        if (Mathf.Abs(horizontalInput) > 0.15f)
        {
            transform.localScale = new Vector3(
                Mathf.Sign(horizontalInput), // 1=朝右，-1=朝左
                1f,                          // Y轴不缩放（避免变形）
                1f                           // Z轴不缩放（2D无用）
            );
        }
    }

    // 调试辅助：在Scene视图绘制接地检测圆（绿色=接地，红色=空中）
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Vector2 checkPos = (Vector2)transform.position + groundCheckOffset;
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);
    }
}