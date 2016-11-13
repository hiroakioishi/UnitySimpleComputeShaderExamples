using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

namespace ParticleSystemAppendConsumeBuffer
{
    #region Structures
    public struct ParticleData
    {
        public Vector3 Velocity;    // 速度
        public Vector3 Position;    // 位置
        public float   Scale;       // スケール
        public float   Age;         // 年齢
        public float   LifeTime;    // 寿命
    };
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

        public int NumToEmit = 64;

        #region Particle Params and Resources
        public Vector3 Gravity = new Vector3(0.0f, -1.0f, 0.0f); // 重力
        public float LifeTimeMin = 5.0f;
        public float LifeTimeMax = 10.0f;

        public Texture2D ParticleTex;          // パーティクルのテクスチャ
        public float ParticleSize = 0.05f; // パーティクルのサイズ
        #endregion

        #region Cameras
        public Camera RenderCam; // パーティクルをレンダリングするカメラ（ビルボードのための逆ビュー行列計算に使用）
        #endregion

        #region ComputeBuffers
        ComputeBuffer particleBufferRead;   // パーティクルのデータを格納するコンピュートバッファ(Consume用) 
        ComputeBuffer particleBufferWrite;  // パーティクルのデータを格納するコンピュートバッファ(Append用)
        ComputeBuffer particleIndirectArgsBuffer;   //    
        #endregion

        #region Private Variables And Resources
        int[] particleIndirectArgs;
        int   currentParticleCount = 0;
        bool isPressedMouseButton;   // マウスのボタンが押されているか
        Vector3 particleEmitPosition = Vector3.zero;   // パーティクルを放出する位置

        Material particleRenderMat;  // パーティクルをレンダリングするマテリアル
        #endregion

        #region MonoBehaviour Functions
        void Start()
        {
            InitResources();
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                isPressedMouseButton = true;
            }
            if (Input.GetMouseButtonUp(0))
            {
                isPressedMouseButton = false;
            }

            if (isPressedMouseButton)
            {
                var mouse = Input.mousePosition;
                mouse.z = 10.0f;
                particleEmitPosition = RenderCam.ScreenToWorldPoint(mouse);

                // パーティクルをエミット
                EmitParticles();
            }

            // 現存するパーティクルの個数を取得
            currentParticleCount = GetCurrentParticleCount();
            // パーティクルを更新
            UpdateParticles();
        }

        void OnRenderObject()
        {
            // 現存するパーティクルの個数を取得
            currentParticleCount = GetCurrentParticleCount();
            // パーティクルを描画
            RenderParticles();
        }

        void OnGUI()
        {
            GUI.skin.label.fontSize = 24;

            GUI.Label(new Rect(0,  0, 1024, 36), "GPUParitlceSystem using Append/ConsumeBuffer");
            GUI.Label(new Rect(0, 32, 1024, 36), "ParticleCount : " + currentParticleCount.ToString("00000") + "/" + NUM_PARTICLES.ToString("00000"));
        }

        void OnDestroy()
        {
            DeleteResources();
        }
        #endregion

        #region Private Functions

        /// <summary>
        /// リソースを初期化
        /// </summary>
        void InitResources()
        {
            // パーティクルのバッファを初期化
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
            if (particleBufferRead != null)
            {
                particleBufferRead.Release();
                particleBufferRead = null;
            }

            if (particleBufferWrite != null)
            {
                particleBufferWrite.Release();
                particleBufferWrite = null;
            }

            if (particleIndirectArgsBuffer != null)
            {
                particleIndirectArgsBuffer.Release();
                particleIndirectArgsBuffer = null;
            }

            if (particleRenderMat != null)
            {
                DestroyImmediate(particleRenderMat);
                particleRenderMat = null;
            }
        }

        /// <summary>
        /// パーティクルを初期化
        /// </summary>
        void InitParticles()
        {
            // パーティクルのコンピュートバッファを作成
            particleBufferRead  = new ComputeBuffer(NUM_PARTICLES, Marshal.SizeOf(typeof(ParticleData)), ComputeBufferType.Append);
            particleBufferWrite = new ComputeBuffer(NUM_PARTICLES, Marshal.SizeOf(typeof(ParticleData)), ComputeBufferType.Append);
            particleBufferRead.SetCounterValue(0);
            particleBufferWrite.SetCounterValue(0);

            particleIndirectArgsBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

            particleIndirectArgs = new int[] { 0, 1, 0, 0 };

            // パーティクルの初期値を設定
            var pData = new ParticleData[NUM_PARTICLES];
            for (int i = 0; i < pData.Length; i++)
            {
                pData[i].Velocity = Random.insideUnitSphere;
                pData[i].Position = Random.insideUnitSphere;
                pData[i].Scale    = 1.0f;
                pData[i].Age      = 0.0f;
            }
            // コンピュートバッファに初期値データをセット
            particleBufferRead.SetData(pData);
            particleBufferWrite.SetData(pData);

            pData = null;
        }
        
        /// <summary>
        /// パーティクルを更新
        /// </summary>
        void UpdateParticles()
        {
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Update");

            cs.SetInt("_CurrentParticleCount", currentParticleCount);
            cs.SetVector("_Gravity", Gravity);
            cs.SetFloat("_DeltaTime", Time.deltaTime);
            cs.SetBuffer(kernelId, "_ParticleBufferRead",  particleBufferRead);
            cs.SetBuffer(kernelId, "_ParticleBufferWrite", particleBufferWrite);
            cs.Dispatch(kernelId, NUM_PARTICLES / NUM_THREAD_X, 1, 1);

            SwapComputeBuffer(ref particleBufferRead, ref particleBufferWrite);
        }

        /// <summary>
        /// パーティクルをエミット
        /// </summary>
        void EmitParticles()
        {
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Emit");

            int numGroups = Mathf.CeilToInt((float)NumToEmit / NUM_THREAD_X);

            // エミット後の個数がパーティクルの総量に満たなければエミット
            if(currentParticleCount + numGroups * NUM_THREAD_X <= NUM_PARTICLES)
            {
                cs.SetFloat ("_Time", Time.time);
                cs.SetVector("_EmitPosition", particleEmitPosition);
                cs.SetFloats("_LifeTimeParams", new float[] { LifeTimeMin, LifeTimeMax });
                cs.SetBuffer(kernelId, "_ParticleBufferWrite", particleBufferRead);
                cs.Dispatch(kernelId, numGroups, 1, 1);
            }
        }

        /// <summary>
        /// パーティクルをレンダリング
        /// </summary>
        void RenderParticles()
        {
            Material m = particleRenderMat;

            // 逆ビュー行列を計算
            var inverseViewMatrix = RenderCam.worldToCameraMatrix.inverse;

            // 各パラメータをセット
            m.SetMatrix("_InvViewMatrix", inverseViewMatrix);
            m.SetTexture("_MainTex", ParticleTex);
            m.SetFloat("_ParticleSize", ParticleSize);
            m.SetBuffer("_ParticleBuffer", particleBufferRead);

            m.SetPass(0);
            Graphics.DrawProceduralIndirect(MeshTopology.Points, particleIndirectArgsBuffer, 0);
        }

        /// <summary>
        /// 現存するパーティクルの個数を取得
        /// </summary>
        /// <returns></returns>
        int GetCurrentParticleCount()
        {
            particleIndirectArgsBuffer.SetData(particleIndirectArgs);
            ComputeBuffer.CopyCount(particleBufferRead, particleIndirectArgsBuffer, 0);
            particleIndirectArgsBuffer.GetData(particleIndirectArgs);
            return particleIndirectArgs[0];
        }

        /// <summary>
        /// バッファを入れ替える
        /// </summary>
        /// <param name="ping"></param>
        /// <param name="pong"></param>
        void SwapComputeBuffer(ref ComputeBuffer ping, ref ComputeBuffer pong)
        {
            ComputeBuffer temp = ping;
            ping = pong;
            pong = temp;
        }
        #endregion
    }
}