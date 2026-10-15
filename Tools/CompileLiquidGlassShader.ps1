param([string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Source) { $Source = Join-Path $root 'App\Resources\Shaders\LiquidGlass.hlsl' }
if (-not $Output) { $Output = Join-Path $root 'App\Resources\Shaders\LiquidGlass.ps' }
# D3DCompiler ships with Windows; compilation is a build-time operation only.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class LertaroGlassShaderCompiler
{
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBlob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
    [DllImport("d3dcompiler_47.dll", CallingConvention=CallingConvention.StdCall)]
    private static extern int D3DCompile(byte[] data, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr macros, IntPtr include, [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string profile, uint flags, uint flags2, out IBlob code, out IBlob errors);
    public static void Compile(string source, string output)
    {
        var bytes=File.ReadAllBytes(source);
        IBlob code, errors;
        int result=D3DCompile(bytes,(UIntPtr)bytes.Length,"Glass.hlsl",IntPtr.Zero,IntPtr.Zero,"main","ps_3_0",1u<<15,0,out code,out errors);
        try
        {
            if(result<0) throw new InvalidOperationException(errors==null ? "Shader compilation failed." : Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
            var compiled=new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(),compiled,0,compiled.Length);
            File.WriteAllBytes(output,compiled);
        }
        finally { if(code!=null) Marshal.ReleaseComObject(code); if(errors!=null) Marshal.ReleaseComObject(errors); }
    }
}
'@
[LertaroGlassShaderCompiler]::Compile([IO.Path]::GetFullPath($Source), [IO.Path]::GetFullPath($Output))
