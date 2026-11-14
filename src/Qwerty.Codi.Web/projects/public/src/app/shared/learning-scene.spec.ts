import { TestBed } from '@angular/core/testing';
import { LearningScene } from './learning-scene';

const graphics = vi.hoisted(() => ({
  render: vi.fn(),
  setAnimationLoop: vi.fn(),
  dispose: vi.fn(),
  forceContextLoss: vi.fn(),
  geometryDispose: vi.fn(),
  materialDispose: vi.fn(),
}));
vi.mock('three', () => {
  class Object3D {
    children: Object3D[] = [];
    position = { x: 0, y: 0, z: 0, set: vi.fn() };
    rotation = { x: 0, y: 0, z: 0 };
    add(child: Object3D) {
      this.children.push(child);
    }
  }
  class Geometry {
    dispose = graphics.geometryDispose;
  }
  return {
    WebGLRenderer: class {
      render = graphics.render;
      setAnimationLoop = graphics.setAnimationLoop;
      dispose = graphics.dispose;
      forceContextLoss = graphics.forceContextLoss;
      setPixelRatio = vi.fn();
      setSize = vi.fn();
    },
    Scene: Object3D,
    Group: Object3D,
    Mesh: Object3D,
    PerspectiveCamera: class extends Object3D {
      aspect = 1;
      updateProjectionMatrix = vi.fn();
    },
    HemisphereLight: Object3D,
    DirectionalLight: Object3D,
    BoxGeometry: Geometry,
    SphereGeometry: Geometry,
    TorusGeometry: Geometry,
    CylinderGeometry: Geometry,
    ExtrudeGeometry: Geometry,
    MeshStandardMaterial: class {
      dispose = graphics.materialDispose;
    },
    Shape: class {
      moveTo = vi.fn();
      lineTo = vi.fn();
      closePath = vi.fn();
    },
  };
});

describe('Decorazioni Three.js', () => {
  let reduceMotion: boolean;
  let visibility: IntersectionObserverCallback;
  let motionChange: () => void;
  const disconnect = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    reduceMotion = false;
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      {} as WebGL2RenderingContext,
    );
    vi.stubGlobal('matchMedia', () => ({
      get matches() {
        return reduceMotion;
      },
      addEventListener: (_: string, listener: () => void) => {
        motionChange = listener;
      },
      removeEventListener: vi.fn(),
    }));
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe = vi.fn();
        disconnect = disconnect;
      },
    );
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(callback: IntersectionObserverCallback) {
          visibility = callback;
        }
        observe = vi.fn();
        disconnect = disconnect;
      },
    );
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  async function mount() {
    const fixture = TestBed.createComponent(LearningScene);
    fixture.detectChanges();
    await vi.waitFor(() => expect(graphics.render).toHaveBeenCalled());
    fixture.detectChanges();
    return fixture;
  }

  it('mostra una scena ferma quando il dispositivo riduce il movimento', async () => {
    reduceMotion = true;
    const fixture = await mount();
    expect(graphics.setAnimationLoop.mock.calls.every(([callback]) => callback === null)).toBe(
      true,
    );
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.scene')?.getAttribute('aria-hidden'),
    ).toBe('true');
  });

  it('termina l’animazione e libera le risorse alla distruzione', async () => {
    const fixture = await mount();
    const animate = graphics.setAnimationLoop.mock.calls.find(
      ([callback]) => typeof callback === 'function',
    )![0];
    animate(100);
    animate(4700);
    expect(graphics.setAnimationLoop).toHaveBeenLastCalledWith(null);
    fixture.destroy();
    expect(graphics.dispose).toHaveBeenCalledTimes(1);
    expect(graphics.forceContextLoss).toHaveBeenCalledTimes(1);
    expect(graphics.geometryDispose).toHaveBeenCalled();
    expect(graphics.materialDispose).toHaveBeenCalled();
    expect(disconnect).toHaveBeenCalledTimes(2);
  });

  it('sospende il movimento fuori schermo e quando cambia la preferenza', async () => {
    await mount();
    visibility(
      [{ isIntersecting: false } as IntersectionObserverEntry],
      {} as IntersectionObserver,
    );
    expect(graphics.setAnimationLoop).toHaveBeenLastCalledWith(null);
    visibility([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);
    expect(graphics.setAnimationLoop).toHaveBeenLastCalledWith(expect.any(Function));
    reduceMotion = true;
    motionChange();
    expect(graphics.setAnimationLoop).toHaveBeenLastCalledWith(null);
  });

  it('conserva il disegno statico quando WebGL non è disponibile', async () => {
    vi.mocked(HTMLCanvasElement.prototype.getContext).mockReturnValue(null);
    const fixture = TestBed.createComponent(LearningScene);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(graphics.render).not.toHaveBeenCalled();
    expect(
      (fixture.nativeElement as HTMLElement)
        .querySelector('.fallback')
        ?.classList.contains('hidden'),
    ).toBe(false);
  });
});
