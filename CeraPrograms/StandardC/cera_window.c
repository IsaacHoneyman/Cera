#include <SDL2/SDL.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

struct WindowContext {
    SDL_Window* win;
    SDL_Renderer* ren;
    SDL_Texture* tex;
    uint32_t* pixel_buffer; 
};


intptr_t extCreateWindow(const char* title, int64_t winW, int64_t winH, int64_t texW, int64_t texH) {
    if (SDL_Init(SDL_INIT_VIDEO) < 0) return 0;    
    SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "0"); 

    SDL_Window* window = SDL_CreateWindow(title, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, 
        winW, winH, SDL_WINDOW_SHOWN);
                                          
    SDL_Renderer* renderer = SDL_CreateRenderer(window, -1, SDL_RENDERER_ACCELERATED | SDL_RENDERER_PRESENTVSYNC);
    
    SDL_Texture* texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_ARGB8888, 
        SDL_TEXTUREACCESS_STREAMING, texW, texH);
    
    struct WindowContext* ctx = malloc(sizeof(struct WindowContext));
    ctx->win = window;
    ctx->ren = renderer;
    ctx->tex = texture;
    
    ctx->pixel_buffer = malloc(texW * texH * sizeof(uint32_t)); 
    
    return (intptr_t)ctx;
}

int64_t extDestroyWindow(intptr_t winPtr) {
    if (!winPtr) return 0;
    struct WindowContext* ctx = (struct WindowContext*)winPtr;
    
    free(ctx->pixel_buffer);
    SDL_DestroyTexture(ctx->tex);
    SDL_DestroyRenderer(ctx->ren);
    SDL_DestroyWindow(ctx->win);
    free(ctx);
    
    SDL_Quit();

    return 0;
}

int64_t extDrawPixels(intptr_t winPtr, int64_t* pixels64, int64_t w, int64_t h) {
    if (!winPtr || !pixels64) return 0;
    struct WindowContext* ctx = (struct WindowContext*)winPtr;
    
    int total_pixels = w * h;
    
    for (int i = 0; i < total_pixels; i++) {
        ctx->pixel_buffer[i] = (uint32_t)pixels64[i];
    }
    
    SDL_UpdateTexture(ctx->tex, NULL, ctx->pixel_buffer, w * sizeof(uint32_t));
    SDL_RenderClear(ctx->ren);
    SDL_RenderCopy(ctx->ren, ctx->tex, NULL, NULL);
    SDL_RenderPresent(ctx->ren);

    return 0;
}

int64_t extClearContext(intptr_t winPtr, double r, double g, double b) {
    if (!winPtr) return 0;
    struct WindowContext* ctx = (struct WindowContext*)winPtr;
    
    SDL_SetRenderDrawColor(ctx->ren, (Uint8)(r * 255.0), (Uint8)(g * 255.0), (Uint8)(b * 255.0), 255);
    SDL_RenderClear(ctx->ren);

    return 0;
}

int64_t extSwapBuffers(intptr_t winPtr) {
    if (!winPtr) return 0;
    struct WindowContext* ctx = (struct WindowContext*)winPtr;
    
    SDL_RenderPresent(ctx->ren);

    return 0;
}

static const Uint8* sdl_keys = NULL;
static Uint8 current_keys[SDL_NUM_SCANCODES];
static Uint8 prev_keys[SDL_NUM_SCANCODES];

static int mouse_x = 0;
static int mouse_y = 0;
static Uint32 current_mouse = 0;
static Uint32 prev_mouse = 0;

intptr_t extPollEvents(intptr_t winPtr) {
    if (!sdl_keys) sdl_keys = SDL_GetKeyboardState(NULL);
    
    memcpy(prev_keys, current_keys, SDL_NUM_SCANCODES);
    prev_mouse = current_mouse;

    int should_close = 0;
    SDL_Event e;
    
    while (SDL_PollEvent(&e)) {
        if (e.type == SDL_QUIT) {
            should_close = 1;
        }
    }

    memcpy(current_keys, sdl_keys, SDL_NUM_SCANCODES);
    current_mouse = SDL_GetMouseState(&mouse_x, &mouse_y);

    return should_close;
}

intptr_t extGetKey(int64_t scancode) {
    return current_keys[scancode] ? 1 : 0;
}

intptr_t extGetKeyDown(int64_t scancode) {
    return (current_keys[scancode] && !prev_keys[scancode]) ? 1 : 0;
}

intptr_t extGetKeyUp(int64_t scancode) {
    return (!current_keys[scancode] && prev_keys[scancode]) ? 1 : 0;
}

intptr_t extGetMouseButton(int64_t button) {
    return (current_mouse & SDL_BUTTON(button)) ? 1 : 0;
}

intptr_t extGetMouseButtonDown(int64_t button) {
    return ((current_mouse & SDL_BUTTON(button)) && !(prev_mouse & SDL_BUTTON(button))) ? 1 : 0;
}

intptr_t extGetMouseX() { return mouse_x; }
intptr_t extGetMouseY() { return mouse_y; }