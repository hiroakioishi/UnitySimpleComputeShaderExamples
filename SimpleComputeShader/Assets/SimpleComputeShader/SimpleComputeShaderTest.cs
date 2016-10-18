using UnityEngine;
using System.Collections;

public class SimpleComputeShaderTest : MonoBehaviour
{
    const int TEXTURE_WIDTH  = 512; // ComputeShaderでの計算結果を書き込むテクスチャバッファの幅
    const int TEXTURE_HEIGHT = 512; // ComputeShaderでの計算結果を書き込むテクスチャバッファの高さ
    const int THREAD_NUM     = 8;   // コンピュートカーネルのスレッド数

    public ComputeShader SimpleComputeShader; // 計算に使用するComputeShader
    RenderTexture result; // ComputeShaderでの計算結果を書き込むテクスチャバッファ

	void Start ()
    {
        // テクスチャバッファの作成
        result = new RenderTexture(TEXTURE_WIDTH, TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGBFloat);
        result.hideFlags         = HideFlags.HideAndDontSave;
        result.filterMode        = FilterMode.Point;
        result.enableRandomWrite = true; // ComputeShaderからの書き込みを可能にするためのRandomAccessフラグ
        result.Create();                 // 実際にRenderTextureオブジェクトを作成

        int kernelId = SimpleComputeShader.FindKernel("CSMain");        // 実行するコンピュートカーネルのidを取得
        SimpleComputeShader.SetTexture(kernelId, "_Result", result);    // テクスチャバッファをセット
        SimpleComputeShader.Dispatch(kernelId, TEXTURE_WIDTH / THREAD_NUM, TEXTURE_HEIGHT / THREAD_NUM, 1); // コンピュートシェーダを実行
	}
	
    void OnDestroy()
    {
        if(result != null)
        {
            // RenderTextureオブジェクトを削除
            DestroyImmediate(result);
        }
    }

    void OnGUI()
    {
        // ComputeShaderでの処理結果を表示
        GUI.DrawTexture(new Rect(0, 0, TEXTURE_WIDTH, TEXTURE_HEIGHT), result);
    }
}

