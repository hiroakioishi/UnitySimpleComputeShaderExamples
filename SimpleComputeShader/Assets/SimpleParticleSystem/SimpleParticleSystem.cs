using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

// パーティクルデータの構造体
public struct ParticleData
{
    public Vector3 Velocity; // 速度
    public Vector3 Position; // 位置
}

public class SimpleParticleSystem : MonoBehaviour
{
    const int NUM_PARTICLES = 32768; // 生成するパーティクルの数

    const int NUM_THREAD_X = 8; // スレッドグループのX成分のスレッド数
    const int NUM_THREAD_Y = 1; // スレッドグループのY成分のスレッド数
    const int NUM_THREAD_Z = 1; // スレッドグループのZ成分のスレッド数
    
    public ComputeShader SimpleParticleComputeShader; // パーティクルの動きを計算するコンピュートシェーダ
    public Shader        SimpleParticleRenderShader;  // パーティクルをレンダリングするシェーダ

    public Vector3 Gravity  = new Vector3(0.0f, -1.0f, 0.0f); // 重力
    public Vector3 AreaSize = Vector3.one * 10.0f;            // パーティクルが存在するエリアのサイズ

    public Texture2D ParticleTex;          // パーティクルのテクスチャ
    public float     ParticleSize = 0.05f; // パーティクルのサイズ

    public Camera RenderCam; // パーティクルをレンダリングするカメラ（ビルボードのための逆ビュー行列計算に使用）
    
    ComputeBuffer particleBuffer;     // パーティクルのデータを格納するコンピュートバッファ 
    Material      particleRenderMat;  // パーティクルをレンダリングするマテリアル

    void Start()
    {
        // パーティクルのコンピュートバッファを作成
        particleBuffer = new ComputeBuffer(NUM_PARTICLES, Marshal.SizeOf(typeof(ParticleData)));
        // パーティクルの初期値を設定
        var pData = new ParticleData[NUM_PARTICLES];
        for (int i = 0; i < pData.Length; i++)
        {
            pData[i].Velocity = Random.insideUnitSphere;
            pData[i].Position = Random.insideUnitSphere;
        }
        // コンピュートバッファに初期値データをセット
        particleBuffer.SetData(pData);

        pData = null;

        // パーティクルをレンダリングするマテリアルを作成
        particleRenderMat = new Material(SimpleParticleRenderShader);
        particleRenderMat.hideFlags = HideFlags.HideAndDontSave;
	}

    void OnRenderObject()
    {
        ComputeShader cs = SimpleParticleComputeShader;
        // スレッドグループ数を計算
        int numThreadGroup = NUM_PARTICLES / NUM_THREAD_X;
        // カーネルIDを取得
        int kernelId = cs.FindKernel("CSMain");
        // 各パラメータをセット
        cs.SetFloat ("_TimeStep", Time.deltaTime);
        cs.SetVector("_Gravity",  Gravity);
        cs.SetFloats("_AreaSize", new float[3] { AreaSize.x, AreaSize.y, AreaSize.z });
        // コンピュートバッファをセット
        cs.SetBuffer(kernelId, "_ParticleBuffer", particleBuffer);
        // コンピュートシェーダを実行
        cs.Dispatch(kernelId, numThreadGroup, 1, 1);

        // 逆ビュー行列を計算
        var inverseViewMatrix = RenderCam.worldToCameraMatrix.inverse;

        Material m = particleRenderMat;
        m.SetPass(0); // レンダリングのためのシェーダパスをセット
        // 各パラメータをセット
        m.SetMatrix ("_InvViewMatrix",  inverseViewMatrix);
        m.SetTexture("_MainTex",        ParticleTex);
        m.SetFloat  ("_ParticleSize",   ParticleSize);
        // コンピュートバッファをセット
        m.SetBuffer ("_ParticleBuffer", particleBuffer);
        // パーティクルをレンダリング
        Graphics.DrawProcedural(MeshTopology.Points, NUM_PARTICLES);
    }

    void OnDestroy()
    {
        if (particleBuffer != null)
        {
            // バッファをリリース（忘れずに！）
            particleBuffer.Release();
        }

        if (particleRenderMat != null)
        {
            // レンダリングのためのマテリアルを削除
            DestroyImmediate(particleRenderMat);
        }
    }
}
