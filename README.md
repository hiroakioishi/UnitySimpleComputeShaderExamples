# SimpleComputeShaderExamples
社内勉強会 ComputeShader入門 サンプルコード

### SimpleComputeShader
DispatchThreadID.xyの値をRenderTextureに書き込むサンプルコード

### SimpleParticleSystem
シンプルなパーティクルシステム

### ParticleSystemAppendConsumeBuffer
Particleのデータを格納するバッファにAppendStructuredBufferを使用<br/>
パーティクルのレンダリングは Graphics.DrawProceduralIndirect()で行う<br/>
書き込み（Append）用と、読み込み（Consume)用に、2つのComputeBufferType.Appendのバッファを用意し、パーティクルの更新時に、ConsumeStructuredBufferから現存するパーティクルのデータを読み取り、寿命を迎えていないものだけAppendStructuredBufferにAppendすることによって、パーティクルを増減させている。<br/>
（パーティクルの更新と、レンダリングの前にそれぞれ現存するパーティクルの個数、Indirect引数取得のためにCopyCountしてるけどあまり良くないかも…）<br/>
<https://www.youtube.com/watch?v=hP5KA9HtRYA>

### ParticleSystemAppendConsumeBufferForDeadList
Particleのデータは通常のComputeBufferに持ち、死んでいるパーティクルのIDを格納するバッファ(DeadList)にAppend/ConsumeStructuredBufferを使った実装。
パーティクルエミット時には、エミット可能なパーティクルのIDをDeadListからConsumeしてくることによって現存するパーティクルにそのIDを加える。パーティクル更新時には、生存しているパーティクルのみを計算し、寿命を迎えた場合、DeadListにAppendする。<br/>
パーティクルのレンダリングは、Graphics.DrawProcedural()で行っている。死んでいるパーティクルはスケール0でレンダリングすることによって、見えないようにしている。<br/>
<https://www.youtube.com/watch?v=vYnIczMLPuQ>
