(module
  (type $t0 (func (param i32)))
  (type $t1 (func))
  (type $t2 (func (result i32)))
  (import "env" "putc_js" (func $putc_js (type $t0)))
  (func $__wasm_call_ctors (type $t1))
  (func $main (export "main") (type $t2) (result i32)
    i32.const 5
    call $putc_js
    i32.const 0)
  (table $T0 1 1 anyfunc)
  (memory $memory (export "memory") 2)
  (global $g0 (mut i32) (i32.const 66560))
  (global $__heap_base (export "__heap_base") i32 (i32.const 66560))
  (global $__data_end (export "__data_end") i32 (i32.const 1024)))