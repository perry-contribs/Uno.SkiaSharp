﻿using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NativePointerDictionary = System.Collections.Concurrent.ConcurrentDictionary<System.IntPtr, SkiaSharp.SKAbstractManagedStream>;

namespace SkiaSharp
{
	public abstract class SKAbstractManagedStream : SKStreamAsset
	{
		private static readonly NativePointerDictionary managedStreams = new NativePointerDictionary ();

		// delegate declarations
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate IntPtr read_delegate (IntPtr managedStreamPtr, IntPtr buffer, IntPtr size);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate IntPtr peek_delegate (IntPtr managedStreamPtr, IntPtr buffer, IntPtr size);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate bool isAtEnd_delegate (IntPtr managedStreamPtr);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate bool hasPosition_delegate (IntPtr managedStreamPtr);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate bool hasLength_delegate (IntPtr managedStreamPtr);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate bool rewind_delegate (IntPtr managedStreamPtr);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate IntPtr getPosition_delegate (IntPtr managedStreamPtr);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate bool seek_delegate (IntPtr managedStreamPtr, IntPtr position);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate bool move_delegate (IntPtr managedStreamPtr, int offset);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate IntPtr getLength_delegate (IntPtr managedStreamPtr);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate IntPtr createNew_delegate (IntPtr managedStreamPtr);
		[UnmanagedFunctionPointer (CallingConvention.Cdecl)]
		internal delegate void destroy_delegate (IntPtr managedStreamPtr);

#if !__WASM__
		// delegate fields
		private static readonly read_delegate fRead;
		private static readonly peek_delegate fPeek;
		private static readonly isAtEnd_delegate fIsAtEnd;
		private static readonly hasPosition_delegate fHasPosition;
		private static readonly hasLength_delegate fHasLength;
		private static readonly rewind_delegate fRewind;
		private static readonly getPosition_delegate fGetPosition;
		private static readonly seek_delegate fSeek;
		private static readonly move_delegate fMove;
		private static readonly getLength_delegate fGetLength;
		private static readonly createNew_delegate fCreateNew;
		private static readonly destroy_delegate fDestroy;
#endif

		private int fromNative;

		static SKAbstractManagedStream ()
		{
#if __WASM__
			//
			// Javascript returns the list of registered methods, then the
			// sk_managedstream_set_delegates gets called through P/Invoke
			// because it is not exported when building for AOT.
			//

			var ret = WebAssembly.Runtime.InvokeJS($"SkiaSharp.SurfaceManager.registerManagedStream();");
			var funcs = ret.Split (new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(f => (IntPtr)int.Parse(f, CultureInfo.InvariantCulture))
				.ToArray();

			if(funcs.Length == 12) {
				SkiaApi.sk_managedstream_set_delegates (
				funcs[0],
				funcs[1],
				funcs[2],
				funcs[3],
				funcs[4],
				funcs[5],
				funcs[6],
				funcs[7],
				funcs[8],
				funcs[9],
				funcs[10],
				funcs[11]);
			} else {
				throw new InvalidOperationException($"Mismatch for registerManagedStream returned values (got {funcs.Length}, expected 12)");
			}

#else
			fRead = new read_delegate (ReadInternal);
			fPeek = new peek_delegate (PeekInternal);
			fIsAtEnd = new isAtEnd_delegate (IsAtEndInternal);
			fHasPosition = new hasPosition_delegate (HasPositionInternal);
			fHasLength = new hasLength_delegate (HasLengthInternal);
			fRewind = new rewind_delegate (RewindInternal);
			fGetPosition = new getPosition_delegate (GetPositionInternal);
			fSeek = new seek_delegate (SeekInternal);
			fMove = new move_delegate (MoveInternal);
			fGetLength = new getLength_delegate (GetLengthInternal);
			fCreateNew = new createNew_delegate (CreateNewInternal);
			fDestroy = new destroy_delegate (DestroyInternal);

			SkiaApi.sk_managedstream_set_delegates (
				Marshal.GetFunctionPointerForDelegate (fRead),
				Marshal.GetFunctionPointerForDelegate (fPeek),
				Marshal.GetFunctionPointerForDelegate (fIsAtEnd),
				Marshal.GetFunctionPointerForDelegate (fHasPosition),
				Marshal.GetFunctionPointerForDelegate (fHasLength),
				Marshal.GetFunctionPointerForDelegate (fRewind),
				Marshal.GetFunctionPointerForDelegate (fGetPosition),
				Marshal.GetFunctionPointerForDelegate (fSeek),
				Marshal.GetFunctionPointerForDelegate (fMove),
				Marshal.GetFunctionPointerForDelegate (fGetLength),
				Marshal.GetFunctionPointerForDelegate (fCreateNew),
				Marshal.GetFunctionPointerForDelegate (fDestroy));
#endif
		}

		protected SKAbstractManagedStream ()
			: this (true)
		{
		}

		protected SKAbstractManagedStream (bool owns)
			: base (SkiaApi.sk_managedstream_new (), owns)
		{
			if (Handle == IntPtr.Zero) {
				throw new InvalidOperationException ("Unable to create a new SKAbstractManagedStream instance.");
			}

			managedStreams.TryAdd (Handle, this);
		}

		internal void DisposeFromNative ()
		{
			Interlocked.Exchange (ref fromNative, 1);
			Dispose ();
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing) {
				managedStreams.TryRemove (Handle, out var managedStream);
			}

			if (Interlocked.CompareExchange (ref fromNative, 0, 0) == 0 && Handle != IntPtr.Zero && OwnsHandle) {
				SkiaApi.sk_managedstream_destroy (Handle);
			}

			base.Dispose (disposing);
		}

		protected internal abstract IntPtr OnRead (IntPtr buffer, IntPtr size);

		protected internal abstract IntPtr OnPeek (IntPtr buffer, IntPtr size);

		protected internal abstract bool OnIsAtEnd ();

		protected internal abstract bool OnHasPosition ();

		protected internal abstract bool OnHasLength ();

		protected internal abstract bool OnRewind ();

		protected internal abstract IntPtr OnGetPosition ();

		protected internal abstract IntPtr OnGetLength ();

		protected internal abstract bool OnSeek (IntPtr position);

		protected internal abstract bool OnMove (int offset);

		protected internal abstract IntPtr OnCreateNew ();

		// unmanaged <-> managed methods (static for iOS)

		[MonoPInvokeCallback (typeof (read_delegate))]
		private static IntPtr ReadInternal (IntPtr managedStreamPtr, IntPtr buffer, IntPtr size)
		{
			return AsManagedStream (managedStreamPtr).OnRead (buffer, size);
		}

		[MonoPInvokeCallback(typeof(peek_delegate))]
		private static IntPtr PeekInternal (IntPtr managedStreamPtr, IntPtr buffer, IntPtr size)
		{
			return AsManagedStream (managedStreamPtr).OnPeek (buffer, size);
		}

		[MonoPInvokeCallback(typeof(isAtEnd_delegate))]
		private static bool IsAtEndInternal (IntPtr managedStreamPtr)
		{
			return AsManagedStream (managedStreamPtr).OnIsAtEnd ();
		}

		[MonoPInvokeCallback(typeof(hasPosition_delegate))]
		private static bool HasPositionInternal (IntPtr managedStreamPtr)
		{
			return AsManagedStream (managedStreamPtr).OnHasPosition ();
		}

		[MonoPInvokeCallback(typeof(hasLength_delegate))]
		private static bool HasLengthInternal (IntPtr managedStreamPtr)
		{
			return AsManagedStream (managedStreamPtr).OnHasLength ();
		}

		[MonoPInvokeCallback(typeof(rewind_delegate))]
		private static bool RewindInternal (IntPtr managedStreamPtr)
		{
			return AsManagedStream (managedStreamPtr).OnRewind ();
		}

		[MonoPInvokeCallback(typeof(getPosition_delegate))]
		private static IntPtr GetPositionInternal (IntPtr managedStreamPtr)
		{
			return AsManagedStream (managedStreamPtr).OnGetPosition ();
		}

		[MonoPInvokeCallback(typeof(seek_delegate))]
		private static bool SeekInternal (IntPtr managedStreamPtr, IntPtr position)
		{
			return AsManagedStream (managedStreamPtr).OnSeek (position);
		}

		[MonoPInvokeCallback(typeof(move_delegate))]
		private static bool MoveInternal (IntPtr managedStreamPtr, int offset)
		{
			return AsManagedStream (managedStreamPtr).OnMove (offset);
		}

		[MonoPInvokeCallback(typeof(getLength_delegate))]
		private static IntPtr GetLengthInternal (IntPtr managedStreamPtr)
		{
			return AsManagedStream (managedStreamPtr).OnGetLength ();
		}

		[MonoPInvokeCallback(typeof(createNew_delegate))]
		private static IntPtr CreateNewInternal (IntPtr managedStreamPtr)
		{
			return AsManagedStream (managedStreamPtr).OnCreateNew ();
		}

		[MonoPInvokeCallback(typeof(destroy_delegate))]
		private static void DestroyInternal (IntPtr managedStreamPtr)
		{
			if (AsManagedStream (managedStreamPtr, out var managedStream)) {
				managedStream.DisposeFromNative ();
			}
		}

		internal static SKAbstractManagedStream AsManagedStream (IntPtr ptr)
		{
			if (AsManagedStream (ptr, out var target)) {
				return target;
			}
			throw new ObjectDisposedException ("SKAbstractManagedStream: " + ptr);
		}

		internal static bool AsManagedStream (IntPtr ptr, out SKAbstractManagedStream target)
		{
			if (managedStreams.TryGetValue (ptr, out target)) {
				return true;
			}
			target = null;
			return false;
		}
	}

	public static class ManagedStreamHelper
	{
		public static int ReadInternal (int managedStreamPtr, int buffer, int size)
		{
			var ret = (int)SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnRead ((IntPtr)buffer, (IntPtr)size);
			return ret;
		}

		public static int PeekInternal (int managedStreamPtr, int buffer, int size)
		{
			var ret = (int)SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnPeek ((IntPtr)buffer, (IntPtr)size);
			return ret;
		}

		public static bool IsAtEndInternal (int managedStreamPtr)
		{
			var ret = SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnIsAtEnd ();
			return ret;
		}

		public static bool HasPositionInternal (int managedStreamPtr)
		{
			var ret = SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnHasPosition ();
			return ret;
		}

		public static bool HasLengthInternal (int managedStreamPtr)
		{
			var ret = SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnHasLength ();
			return ret;
		}

		public static bool RewindInternal (int managedStreamPtr)
		{
			var ret = SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnRewind ();
			return ret;
		}

		public static int GetPositionInternal (int managedStreamPtr)
		{
			var ret = (int)SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnGetPosition ();
			return ret;
		}

		public static bool SeekInternal (int managedStreamPtr, int position)
		{
			var ret = SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnSeek ((IntPtr)position);
			return ret;
		}

		public static bool MoveInternal (int managedStreamPtr, int offset)
		{
			var ret = SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnMove (offset);
			return ret;
		}

		public static int GetLengthInternal (int managedStreamPtr)
		{
			var ret = (int)SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnGetLength ();
			return ret;
		}

		public static int CreateNewInternal (int managedStreamPtr)
		{
			var ret = (int)SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr).OnCreateNew ();
			return ret;
		}

		public static void DestroyInternal (int managedStreamPtr)
		{
			if (SKAbstractManagedStream.AsManagedStream ((IntPtr)managedStreamPtr, out var managedStream)) {
				managedStream.DisposeFromNative ();
			}
		}
	}
}
