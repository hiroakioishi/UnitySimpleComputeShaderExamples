using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

namespace ParticleSystemAppendConsumeBufferForDeadList
{
    #region Structures
    // パーティクルデータの構造体
    public struct ParticleData
    {
        public Vector3 Velocity;    // 速度
        public Vector3 Position;    // 位置
        public float   Scale;       // スケール
        public float   Age;         // 生成されてからの経過時間
        public float   LifeTime;    // 寿命
        public bool    Alive;       // 生死
    }
    #endregion

    public class GPUParticleSystem : MonoBehaviour
    {
        #region Constants
        const int NUM_PARTICLES = 65536; // 生成するパーティクルの数

        const int NUM_THREAD_X = 8; // スレッドグループのX成分のスレッド数
        const int NUM_THREAD_Y = 1; // スレッドグループのY成分のスレッド数
        const int NUM_THREAD_Z = 1; // スレッドグループのZ成分のスレッド数
        #endregion

        #region Shader Resources
        public ComputeShader SimpleParticleComputeShader; // パーティクルの動きを計算するコンピュートシェーダ
        public Shader        SimpleParticleRenderShader;  // パーティクルをレンダリングするシェーダ
        #endregion

        #region Particle Params And Resources
        public Vector3   Gravity      = new Vector3(0.0f, -1.0f, 0.0f); // 重力
        public float     LifeTimeMin  =  1.0f;                          // 寿命の最小値
        public float     LifeTimeMax  = 10.0f;                          // 寿命の最大値
        public Texture2D ParticleTex;                                   // パーティクルのテクスチャ
        public float     ParticleSize = 0.05f;                          // パーティクルのサイズ
        #endregion

        #region Cameras
        public Camera RenderCam; // パーティクルをレンダリングするカメラ（ビルボードのための逆ビュー行列計算に使用）
        #endregion

        #region ComputeBuffers
        ComputeBuffer particleBuffer;               // パーティクルのデータを格納するコンピュートバッファ
        ComputeBuffer particleDeadListBuffer;       // 死んでいるパーティクルのIDを格納するコンピュートバッファ
        ComputeBuffer particleIndirectArgsBuffer;   // Indirect引数を格納するコンピュートバッファ
        #endregion

        #region Private Variables
        int[]   particleIndirectArgs;   // Indirect引数を格納するための配列
        bool    isPressedMouseButton;   // マウスのボタンが押されているか
        Vector3 particleEmitPosition = Vector3.zero;   // パーティクルを放出する位置
        #endregion

        #region Materials
        Material particleRenderMat;                 // パーティクルをレンダリングするマテリアル
        #endregion

        #region MonoBehaviour Functions
        void Start()
        {
            // リソースの初期化
            InitResources();
        }

        void Update()
        {
            
            if(Input.GetMouseButtonDown(0))
            {
                isPressedMouseButton = true;
            }
            if(Input.GetMouseButtonUp(0))
            {
                isPressedMouseButton = false;
            }
            
            if(isPressedMouseButton)
            {
                var mouse = Input.mousePosition;
                mouse.z = 10.0f;
                particleEmitPosition = RenderCam.ScreenToWorldPoint(mouse);

                // パーティクルをエミット
                EmitParticles();
            }

            // パーティクルを更新
            UpdateParticles();
        }

        void OnRenderObject()
        {
            // パーティクルをレンダリング
            RenderParticles();
        }

        void OnDestroy()
        {
            DeleteResources();
        }

        void OnGUI()
        {
            GUI.skin.label.fontSize = 24;

            GUI.Label(new Rect(0,  0, 1024, 36), "GPUParitlceSystem using Append/ConsumeBuffer for DeadParticleList");
            GUI.Label(new Rect(0, 32, 1024, 36), "DeadListParticleCount : " + GetParticleDeadListSize().ToString("00000") + "/" + NUM_PARTICLES.ToString("00000"));
        }
        #endregion

        #region Private Functions
        /// <summary>
        /// リソースの初期化
        /// </summary>
        void InitResources()
        {
            // パーティクルを初期化
            InitParticles();

            // パーティクルをレンダリングするマテリアルを作成
            particleRenderMat = new Material(SimpleParticleRenderShader);
            particleRenderMat.hideFlags = HideFlags.HideAndDontSave;
        }

        /// <summary>
        /// リソースを削除
        /// </summary>
        void DeleteResources()
        {
            if (particleBuffer != null)
            {
                // バッファをリリース
                particleBuffer.Release();
            }

            if (particleDeadListBuffer != null)
            {
                // バッファをリリース
                particleDeadListBuffer.Release();
            }

            if (particleIndirectArgsBuffer != null)
            {
                // バッファをリリース
                particleIndirectArgsBuffer.Release();
            }

            if (particleRenderMat != null)
            {
                // レンダリングのためのマテリアルを削除
                DestroyImmediate(particleRenderMat);
            }
        }

        /// <summary>
        /// パーティクルを初期化
        /// </summary>
        void InitParticles()
        {
            // パーティクルのコンピュートバッファを作成
            particleBuffer = new ComputeBuffer(NUM_PARTICLES, Marshal.SizeOf(typeof(ParticleData)));
            // パーティクルの初期値を設定
            var pData = new ParticleData[NUM_PARTICLES];
            for (int i = 0; i < pData.Length; i++)
            {
                pData[i].Velocity = Random.insideUnitSphere;
                pData[i].Position = Random.insideUnitSphere;
                pData[i].Scale    = 0.0f;
                pData[i].Age      = 0.0f;
                pData[i].Alive    = false;
            }
            // コンピュートバッファに初期値データをセット
            particleBuffer.SetData(pData);

            pData = null;

            // DeadListのためのバッファを作成
            particleDeadListBuffer = new ComputeBuffer(NUM_PARTICLES, sizeof(int), ComputeBufferType.Append);
            particleDeadListBuffer.SetCounterValue(0);
            // IndirectArgumentsのバッファを作成
            particleIndirectArgsBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

            particleIndirectArgs = new int[] { 0, 1, 0, 0 };

            // バッファを初期化
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Init");
            cs.SetBuffer(kernelId, "_ParticleBuffer", particleBuffer);
            cs.SetBuffer(kernelId, "_ParticleDeadListBufferAppend", particleDeadListBuffer);
            cs.Dispatch(kernelId, NUM_PARTICLES / NUM_THREAD_X, 1, 1);
        }

        /// <summary>
        /// パーティクルをエミット
        /// </summary>
        void EmitParticles()
        {
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Emit");
            cs.SetFloat("_Time", Time.time);
            cs.SetVector("_EmitPosition", particleEmitPosition);
            cs.SetFloats("_LifeTimeParams", new float[] { LifeTimeMin, LifeTimeMax });
            cs.SetBuffer(kernelId, "_ParticleBuffer",                particleBuffer);
            cs.SetBuffer(kernelId, "_ParticleDeadListBufferConsume", particleDeadListBuffer);
            // 生成されたスレッドの数だけパーティクルをエミット
            cs.Dispatch(kernelId, Mathf.Min(10, GetParticleDeadListSize() / NUM_THREAD_X), 1, 1);
        }

        /// <summary>
        /// パーティクルを更新
        /// </summary>
        void UpdateParticles()
        {
            ComputeShader cs = SimpleParticleComputeShader;
            // スレッドグループ数を計算
            int numThreadGroup = NUM_PARTICLES / NUM_THREAD_X;
            // カーネルIDを取得
            int kernelId = cs.FindKernel("Update");
            // 各パラメータをセット
            cs.SetFloat("_DeltaTime", Time.deltaTime);
            cs.SetVector("_Gravity", Gravity);
            // コンピュートバッファをセット
            cs.SetBuffer(kernelId, "_ParticleBuffer", particleBuffer);
            cs.SetBuffer(kernelId, "_ParticleDeadListBufferAppend", particleDeadListBuffer);
            // コンピュートシェーダを実行
            cs.Dispatch(kernelId, numThreadGroup, 1, 1);
        }

        /// <summary>
        /// パーティクルをレンダリング
        /// </summary>
        void RenderParticles()
        {
            // 逆ビュー行列を計算
            var inverseViewMatrix = RenderCam.worldToCameraMatrix.inverse;

            Material m = particleRenderMat;
            m.SetPass(0); // レンダリングのためのシェーダパスをセット
            // 各パラメータをセット
            m.SetMatrix("_InvViewMatrix", inverseViewMatrix);
            m.SetTexture("_MainTex", ParticleTex);
            m.SetFloat("_ParticleSize", ParticleSize);
            // コンピュートバッファをセット
            m.SetBuffer("_ParticleBuffer", particleBuffer);
            // パーティクルをレンダリング
            Graphics.DrawProcedural(MeshTopology.Points, NUM_PARTICLES);
        }

        /// <summary>
        /// DeadListのサイズ（≒エミットできる残りのパーティクルの数）を得る
        /// </summary>
        /// <returns></returns>
        int GetParticleDeadListSize()
        {
            particleIndirectArgsBuffer.SetData(particleIndirectArgs);
            ComputeBuffer.CopyCount(particleDeadListBuffer, particleIndirectArgsBuffer, 0);
            particleIndirectArgsBuffer.GetData(particleIndirectArgs);
            return particleIndirectArgs[0];
        }
        #endregion
    }
}