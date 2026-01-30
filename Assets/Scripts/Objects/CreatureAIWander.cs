using UnityEngine;
using Mirror;

namespace Controller
{
    [RequireComponent(typeof(CreatureMover))]
    public class CreatureAIWander : NetworkBehaviour
    {
        public enum WanderState { Idle, Walking, Running }

        [Header("状态权重 (总和建议为1)")]
        [Range(0, 1)] public float m_IdleWeight = 0.4f;   // 停下的概率
        [Range(0, 1)] public float m_WalkWeight = 0.4f;   // 走路的概率
        [Range(0, 1)] public float m_RunWeight = 0.2f;    // 跑步的概率

        [Header("时间设置")]
        [SerializeField] private float m_IdleTimeMin = 2f;
        [SerializeField] private float m_IdleTimeMax = 5f;
        [SerializeField] private float m_MoveTimeMin = 3f;
        [SerializeField] private float m_MoveTimeMax = 6f;

        private CreatureMover m_Mover;
        private float m_Timer;
        private WanderState m_CurrentState = WanderState.Idle;
        private Vector2 m_MoveInput;

        private void Awake()
        {
            m_Mover = GetComponent<CreatureMover>();
            SelectNextState();
        }
        [ServerCallback] 
        private void Update()
        {
            m_Timer -= Time.deltaTime;

            if (m_Timer <= 0)
            {
                SelectNextState();
            }

            // 根据当前状态决定输入
            bool isRunning = (m_CurrentState == WanderState.Running);
            Vector2 currentInput = (m_CurrentState == WanderState.Idle) ? Vector2.zero : m_MoveInput;

            // 虚拟目标点（用于控制转向）
            Vector3 virtualTarget = transform.position + new Vector3(m_MoveInput.x, 0, m_MoveInput.y) * 5f;

            if (m_Mover != null)
            {
                // 调用 Mover 接口
                // 第三个参数 isRun 为 true 时，CreatureMover 会把 Animator 的 State 设为 1
                m_Mover.SetInput(currentInput, virtualTarget, isRunning, false);
            }
        }

        private void SelectNextState()
        {
            float roll = Random.value;

            if (roll < m_IdleWeight)
            {
                // 进入 Idle
                m_CurrentState = WanderState.Idle;
                m_Timer = Random.Range(m_IdleTimeMin, m_IdleTimeMax);
            }
            else if (roll < m_IdleWeight + m_WalkWeight)
            {
                // 进入 Walking
                m_CurrentState = WanderState.Walking;
                m_Timer = Random.Range(m_MoveTimeMin, m_MoveTimeMax);
                GenerateRandomDirection();
            }
            else
            {
                // 进入 Running
                m_CurrentState = WanderState.Running;
                m_Timer = Random.Range(m_MoveTimeMin * 0.7f, m_MoveTimeMax * 0.7f); // 跑步时间通常稍短
                GenerateRandomDirection();
            }
        }

        private void GenerateRandomDirection()
        {
            m_MoveInput = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
        }
    }
}