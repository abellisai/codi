import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';

/** Decorazione locale: il contenuto resta disponibile anche senza WebGL. */
@Component({
  selector: 'app-learning-scene',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="scene" [class.small]="celebrate()" aria-hidden="true">
      <div class="fallback" [class.hidden]="ready()">{{ celebrate() ? '★ ✦ ★' : '◕‿◕' }}</div>
      <canvas #canvas [class.visible]="ready()"></canvas>
    </div>
  `,
  styles: `
    :host {
      display: block;
      pointer-events: none;
    }
    .scene {
      position: relative;
      height: 300px;
      width: 100%;
    }
    .small {
      height: 110px;
    }
    canvas {
      display: block;
      width: 100%;
      height: 100%;
      opacity: 0;
    }
    canvas.visible {
      opacity: 1;
    }
    .fallback {
      position: absolute;
      inset: 0;
      display: grid;
      place-items: center;
      font-size: 5rem;
      font-weight: 800;
      color: #6041c6;
    }
    .small .fallback {
      font-size: 3rem;
      color: #997200;
    }
    .hidden {
      display: none;
    }
  `,
})
export class LearningScene {
  readonly celebrate = input(false);
  protected readonly ready = signal(false);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    afterNextRender(() => void this.mount());
  }

  private async mount(): Promise<void> {
    if (
      typeof window.matchMedia !== 'function' ||
      typeof ResizeObserver === 'undefined' ||
      typeof IntersectionObserver === 'undefined'
    )
      return;
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let cleanup = () => {
      /* Nessuna risorsa prima del renderer. */
    };
    this.destroyRef.onDestroy(() => cleanup());
    try {
      const THREE = await import('three');
      if (this.destroyRef.destroyed) return;
      const canvas = this.canvas().nativeElement;
      if (!canvas.getContext('webgl2')) return;
      const renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true });
      const geometries: import('three').BufferGeometry[] = [];
      const materials: import('three').Material[] = [];
      let resize: ResizeObserver | undefined = undefined;
      let observer: IntersectionObserver | undefined = undefined;
      let updateAnimation = () => {
        /* La scena non è ancora pronta. */
      };
      // Cleanup disponibile anche se la costruzione della scena non termina.
      cleanup = () => {
        renderer.setAnimationLoop(null);
        resize?.disconnect();
        observer?.disconnect();
        document.removeEventListener('visibilitychange', updateAnimation);
        motion.removeEventListener('change', updateAnimation);
        geometries.forEach((geometry) => geometry.dispose());
        materials.forEach((material) => material.dispose());
        renderer.dispose();
        renderer.forceContextLoss();
      };
      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
      const scene = new THREE.Scene();
      const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 50);
      camera.position.set(0, 0.3, 6.5);
      scene.add(new THREE.HemisphereLight(0xffffff, 0xb8a6df, 3));
      const light = new THREE.DirectionalLight(0xffffff, 3);
      light.position.set(-3, 5, 5);
      scene.add(light);
      const mascot = new THREE.Group();
      scene.add(mascot);
      const mesh = (
        geometry: import('three').BufferGeometry,
        color: number,
        parent: import('three').Object3D = mascot,
      ) => {
        const material = new THREE.MeshStandardMaterial({ color, roughness: 0.45 });
        geometries.push(geometry);
        materials.push(material);
        const item = new THREE.Mesh(geometry, material);
        parent.add(item);
        return item;
      };
      if (!this.celebrate()) {
        mesh(new THREE.BoxGeometry(1.55, 1.35, 1.05), 0x8061da).rotation.z = -0.06;
        mesh(new THREE.BoxGeometry(1.27, 0.85, 0.12), 0xf5f7ff).position.set(0, 0.1, 0.57);
        for (const x of [-0.3, 0.3]) {
          mesh(new THREE.SphereGeometry(0.12, 16, 12), 0x243553).position.set(x, 0.2, 0.69);
          mesh(new THREE.BoxGeometry(0.38, 0.25, 0.55), 0x54bd9c).position.set(
            x * 1.5,
            -0.85,
            0.15,
          );
        }
        const smile = mesh(new THREE.TorusGeometry(0.18, 0.035, 8, 20, Math.PI), 0x243553);
        smile.position.set(0, 0, 0.7);
        smile.rotation.z = Math.PI;
        mesh(new THREE.CylinderGeometry(0.04, 0.04, 0.4), 0x6041c6).position.y = 0.85;
        mesh(new THREE.SphereGeometry(0.16, 16, 12), 0xffca57).position.y = 1.1;
        for (const side of [-1, 1]) {
          mesh(new THREE.SphereGeometry(0.24, 16, 12), 0x54bd9c).position.set(
            side * 1.05,
            side > 0 ? 0.45 : -0.2,
            0,
          );
        }
        mascot.rotation.y = -0.2;
      }
      const stars = new THREE.Group();
      scene.add(stars);
      for (let i = 0; i < (this.celebrate() ? 11 : 6); i++) {
        const shape = new THREE.Shape();
        for (let point = 0; point < 10; point++) {
          const angle = Math.PI / 2 + (point * Math.PI) / 5;
          const radius = point % 2 === 0 ? 0.23 : 0.11;
          const x = Math.cos(angle) * radius;
          const y = Math.sin(angle) * radius;
          if (point === 0) shape.moveTo(x, y);
          else shape.lineTo(x, y);
        }
        shape.closePath();
        const star = mesh(
          new THREE.ExtrudeGeometry(shape, { depth: 0.08, bevelEnabled: false }),
          [0xffcb59, 0x7d61d1, 0x54bd9c][i % 3]!,
          stars,
        );
        const angle = i * 2.399;
        star.position.set(Math.cos(angle) * (1.5 + (i % 2) * 0.5), Math.sin(angle) * 1.3, 0);
      }
      let start: number | null = null;
      let lastFrame = 0;
      let finished = false;
      const render = (time: number) => {
        if (time - lastFrame < 1000 / 30) return;
        lastFrame = time;
        start ??= time;
        const elapsed = (time - start) / 1000;
        mascot.position.y = Math.sin(elapsed * 2) * 0.12;
        mascot.rotation.z = Math.sin(elapsed * 1.5) * 0.06;
        stars.rotation.z = Math.sin(elapsed * 0.8) * 0.12;
        for (const star of stars.children) star.rotation.y = elapsed * 0.4;
        renderer.render(scene, camera);
        if (elapsed >= 4.5) {
          finished = true;
          renderer.setAnimationLoop(null);
        }
      };
      let visible = true;
      updateAnimation = () => {
        renderer.setAnimationLoop(null);
        if (!motion.matches && !document.hidden && visible && !finished)
          renderer.setAnimationLoop(render);
        renderer.render(scene, camera);
      };
      resize = new ResizeObserver(() => {
        const { width, height } = canvas.getBoundingClientRect();
        if (width <= 0 || height <= 0) return;
        renderer.setSize(width, height, false);
        camera.aspect = width / height;
        camera.updateProjectionMatrix();
        renderer.render(scene, camera);
      });
      observer = new IntersectionObserver(([entry]) => {
        visible = entry?.isIntersecting ?? false;
        updateAnimation();
      });
      resize.observe(canvas);
      observer.observe(canvas);
      document.addEventListener('visibilitychange', updateAnimation);
      motion.addEventListener('change', updateAnimation);
      this.ready.set(true);
      updateAnimation();
    } catch {
      cleanup();
      this.ready.set(false);
    }
  }
}
