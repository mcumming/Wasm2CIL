/* External function that is implemented in JavaScript. */
extern void putc_js(int t);

#define WASM_EXPORT __attribute__((visibility("default")))

WASM_EXPORT
int main(void) {
	putc_js(5);
}