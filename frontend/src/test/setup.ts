import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { resetViewCacheForTests } from '../viewQueryCache';

afterEach(() => resetViewCacheForTests());
