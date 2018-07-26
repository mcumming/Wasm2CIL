
all: test

.PHONY: test
test: dis

.PHONY: dis-factorial
dis-factorial: FactorialProxy.dll 
	monodis FactorialProxy.dll

FactorialProxy.dll: if.wasm Makefile
	mono --debug ../../Parser.exe factorial.wasm Factorial

FactorialHost.exe: FactorialHost.cs Makefile FactorialProxy.dll
    mcs -debug FactorialHost.cs -reference:FactorialProxy.dll