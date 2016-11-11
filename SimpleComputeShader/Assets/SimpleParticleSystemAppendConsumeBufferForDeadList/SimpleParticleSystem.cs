using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

namespace SimpleParticleSystemAppendConsumeBufferForDeadList
{
    // パーティクルデータの構造体
    public struct ParticleData
    {
        public Vector3 Velocity;    // 速度
        public Vector3 Position;    // 位置
        public float   Scale;       // スケール
        public int     LifeTimer;   // 生成されてからの経過時間
        public bool    Alive;       // 生死
    }

    public class SimpleParticleSystem : MonoBehaviour
    {

        const int NUM_PARTICLES = 32768; // 生成するパーティクルの数

        const int NUM_THREAD_X = 8; // スレッドグループのX成分のスレッド数
        const int NUM_THREAD_Y = 1; // スレッドグループのY成分のスレッド数
        const int NUM_THREAD_Z = 1; // スレッドグループのZ成分のスレッド数

        public ComputeShader SimpleParticleComputeShader; // パーティクルの動きを計算するコンピュートシェーダ
        public Shader SimpleParticleRenderShader;         // パーティクルをレンダリングするシェーダ

        public Vector3 Gravity = new Vector3(0.0f, -1.0f, 0.0f); // 重力
        public Vector3 AreaSize = Vector3.one * 10.0f;           // パーティクルが存在するエリアのサイズ
        public float LifeTimeMin =  1.0f;
        public float LifeTimeMax = 10.0f;

        public Texture2D ParticleTex;      // パーティクルのテクスチャ
        public float ParticleSize = 0.05f; // パーティクルのサイズ

        public Camera RenderCam; // パーティクルをレンダリングするカメラ（ビルボードのための逆ビュー行列計算に使用）

        ComputeBuffer particleBuffer;               // パーティクルのデータを格納するコンピュートバッファ
        ComputeBuffer particleDeadListBuffer;       // 死んでいるパーティクルのIDを格納するコンピュートバッファ
        ComputeBuffer particleIndirectArgsBuffer;   // Graphics.DrawProceduralIndirect時に使用する引数を格納するコンピュートバッファ

        int[] particleIndirectArgs;

        Material particleRenderMat;  // パーティクルをレンダリングするマテリアル

        void Start()
        {
            // パーティクルを初期化
            InitParticles();

            // パーティクルをレンダリングするマテリアルを作成
            particleRenderMat = new Material(SimpleParticleRenderShader);
            particleRenderMat.hideFlags = HideFlags.HideAndDontSave;
        }

        void Update()
        {
            if(Input.GetKey(KeyCode.E))
            {
                // パーティクルをエミット
                EmitParticles();
                Debug.Log("Emit");
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
                pData[i].Alive    = false;
            }
            // コンピュートバッファに初期値データをセット
            particleBuffer.SetData(pData);

            pData = null;

            particleDeadListBuffer = new ComputeBuffer(NUM_PARTICLES, sizeof(int), ComputeBufferType.Append);
            particleDeadListBuffer.SetCounterValue(0);
            particleIndirectArgsBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

            particleIndirectArgs = new int[] { 0, 1, 0, 0 };

            // バッファを初期化
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Init");
            cs.SetBuffer(kernelId, "_ParticleBuffer", particleBuffer);
            cs.SetBuffer(kernelId, "_ParticleDeadListBufferAppend", particleDeadListBuffer);
            cs.Dispatch(kernelId, NUM_PARTICLES / NUM_THREAD_X, 1, 1);
        }

        void EmitParticles()
        {
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Emit");
            cs.SetBuffer(kernelId, "_ParticleBuffer",                particleBuffer);
            cs.SetBuffer(kernelId, "_ParticleDeadListBufferConsume", particleDeadListBuffer);
            cs.Dispatch(kernelId, Mathf.Min(10, GetParticleDeadListSize() / NUM_THREAD_X), 1, 1);
        }

        void UpdateParticles()
        {
            ComputeShader cs = SimpleParticleComputeShader;
            // スレッドグループ数を計算
            int numThreadGroup = NUM_PARTICLES / NUM_THREAD_X;
            // カーネルIDを取得
            int kernelId = cs.FindKernel("Update");
            // 各パラメータをセット
            cs.SetFloat("_TimeStep", Time.deltaTime);
            cs.SetVector("_Gravity", Gravity);
            cs.SetFloats("_AreaSize", new float[3] { AreaSize.x, AreaSize.y, AreaSize.z });
            cs.SetFloats("_LifeTimeParams", new float[] { LifeTimeMin, LifeTimeMax });
            // コンピュートバッファをセット
            cs.SetBuffer(kernelId, "_ParticleBuffer", particleBuffer);
            cs.SetBuffer(kernelId, "_ParticleDeadListBufferAppend", particleDeadListBuffer);
            // コンピュートシェーダを実行
            cs.Dispatch(kernelId, numThreadGroup, 1, 1);
        }

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

        int GetParticleDeadListSize()
        {
            particleIndirectArgsBuffer.SetData(particleIndirectArgs);
            ComputeBuffer.CopyCount(particleDeadListBuffer, particleIndirectArgsBuffer, 0);
            particleIndirectArgsBuffer.GetData(particleIndirectArgs);
            return particleIndirectArgs[0];
        }
    }
}