using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

namespace SimpleParticleSystemAppendConsumeBuffer
{
    public struct ParticleData
    {
        public Vector3 Velocity;
        public Vector3 Position;
        public float   Scale;
        public float   Age;
    };
    
    public class SimpleParticleSystem : MonoBehaviour
    {
        const int NUM_PARTICLES = 65536; // 生成するパーティクルの数

        const int NUM_THREAD_X = 8; // スレッドグループのX成分のスレッド数
        const int NUM_THREAD_Y = 1; // スレッドグループのY成分のスレッド数
        const int NUM_THREAD_Z = 1; // スレッドグループのZ成分のスレッド数

        public ComputeShader SimpleParticleComputeShader; // パーティクルの動きを計算するコンピュートシェーダ
        public Shader SimpleParticleRenderShader;  // パーティクルをレンダリングするシェーダ

        public int NumToEmit = 64;

        public Vector3 Gravity = new Vector3(0.0f, -1.0f, 0.0f); // 重力
        public Vector3 AreaSize = Vector3.one * 10.0f;            // パーティクルが存在するエリアのサイズ
        public float LifeTimeMin = 5.0f;
        public float LifeTimeMax = 10.0f;

        public Texture2D ParticleTex;          // パーティクルのテクスチャ
        public float ParticleSize = 0.05f; // パーティクルのサイズ

        public Camera RenderCam; // パーティクルをレンダリングするカメラ（ビルボードのための逆ビュー行列計算に使用）

        ComputeBuffer particleBufferRead;   // パーティクルのデータを格納するコンピュートバッファ(Consume用) 
        ComputeBuffer particleBufferWrite;  // パーティクルのデータを格納するコンピュートバッファ(Append用)
        ComputeBuffer particleIndirectArgsBuffer;   //    

        int[] particleIndirectArgs;
        int   currentParticleCount = 0;

        Material particleRenderMat;  // パーティクルをレンダリングするマテリアル

        void Start()
        {
            InitParticles();
        }

        void Update()
        {
            if(Input.GetKey("e"))
            {
                EmitParticles();
            }

            currentParticleCount = GetCurrentParticleCount();
            UpdateParticles();
        }

        void OnRenderObject()
        {
            currentParticleCount = GetCurrentParticleCount();
            RenderParticles();
        }

        void OnGUI()
        {
            GUI.skin.label.fontSize = 32;
            if (currentParticleCount >= NUM_PARTICLES)
                GUI.Label(new Rect(0, 0, 512, 36), "<color=red>" + currentParticleCount.ToString("00000") + "</color>");
            else
                GUI.Label(new Rect(0, 0, 512, 36), currentParticleCount.ToString("00000"));
        }

        void OnDestroy()
        {
            DeleteResources();
        }

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

            // パーティクルをレンダリングするマテリアルを作成
            particleRenderMat = new Material(SimpleParticleRenderShader);
            particleRenderMat.hideFlags = HideFlags.HideAndDontSave;
        }
        
        void UpdateParticles()
        {
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Update");
            cs.SetInt("_CurrentParticleCount", currentParticleCount);
            cs.SetFloats("_LifeTimeParams", new float[] { LifeTimeMin, LifeTimeMax });
            cs.SetVector("_Gravity", Gravity);
            cs.SetFloat("_TimeStep", Time.deltaTime);
            cs.SetBuffer(kernelId, "_ParticleBufferRead",  particleBufferRead);
            cs.SetBuffer(kernelId, "_ParticleBufferWrite", particleBufferWrite);
            cs.Dispatch(kernelId, NUM_PARTICLES / NUM_THREAD_X, 1, 1);

            SwapComputeBuffer(ref particleBufferRead, ref particleBufferWrite);
        }

        void EmitParticles()
        {
            var cs = SimpleParticleComputeShader;
            var kernelId = cs.FindKernel("Emit");

            int numGroups = Mathf.CeilToInt((float)NumToEmit / NUM_THREAD_X);

            if(currentParticleCount + numGroups * NUM_THREAD_X <= NUM_PARTICLES)
            {
                cs.SetFloat("_Time", Time.time);
                cs.SetBuffer(kernelId, "_ParticleBufferWrite", particleBufferRead);
                cs.Dispatch(kernelId, numGroups, 1, 1);
                Debug.Log("Emit");
            }
        }

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

        int GetCurrentParticleCount()
        {
            particleIndirectArgsBuffer.SetData(particleIndirectArgs);
            ComputeBuffer.CopyCount(particleBufferRead, particleIndirectArgsBuffer, 0);
            particleIndirectArgsBuffer.GetData(particleIndirectArgs);
            return particleIndirectArgs[0];
        }

        void SwapComputeBuffer(ref ComputeBuffer ping, ref ComputeBuffer pong)
        {
            ComputeBuffer temp = ping;
            ping = pong;
            pong = temp;
        }

        void DeleteResources()
        {
            if(particleBufferRead != null)
            {
                particleBufferRead.Release();
                particleBufferRead = null;
            }

            if(particleBufferWrite != null)
            {
                particleBufferWrite.Release();
                particleBufferWrite = null;
            }

            if(particleIndirectArgsBuffer != null)
            {
                particleIndirectArgsBuffer.Release();
                particleIndirectArgsBuffer = null;
            }

            if(particleRenderMat != null)
            {
                DestroyImmediate(particleRenderMat);
                particleRenderMat = null;
            }
        }

    }
}